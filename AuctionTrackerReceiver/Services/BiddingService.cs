using RabbitMQ.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using AuctionTrackerReceiver.Models;
using System.Text;
using System.Text.Json;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using StackExchange.Redis;

namespace AuctionTrackerReceiver.Services;

public interface IBiddingService
{
    Task<bool> BuyOut(Bid buyoutbid);
    Task<bool> CheckCache(Bid bid);
    Task<bool> CheckCatalog(Bid bid);
    Task<Wrapper> FetchWrapper(string catalogid);
    Bid PostBid(Bid data);
    void UpdateCache(string catalogid, double price, DateTime endtime);
    void UpdateCache(string catalogid, double price);
    Task<bool> UpdateDatabaseTime(string catalogId, DateTime endTime);
}

public class BiddingService : IBiddingService
{
    // Attributter
    private readonly ILogger<BiddingService> _logger;
    private readonly IConfiguration _config;
    private readonly IModel _channel;
    private readonly string RabbitHostName;
    private readonly string RabbitQueue;
    private readonly string CatalogHTTPBase;
    private readonly string RedisConnection;

    // Constructor
    public BiddingService(ILogger<BiddingService> logger, IConfiguration config)
    {
        _config = config;
        _logger = logger;
        RabbitHostName = _config["rabbithostname"];
        RabbitQueue = _config["rabbitqueue"];
        CatalogHTTPBase = _config["cataloghttpbase"];
        RedisConnection = _config["redisconnection"];
        var factory = new ConnectionFactory() { HostName = RabbitHostName };
        var connection = factory.CreateConnection();
        _channel = connection.CreateModel();
    }

    // Sender en ny budbesked til RabbitMQ-køen og returnerer det oprettede budobjekt
    public Bid PostBid(Bid data)
    {
        Bid newBid = data;
        _channel.QueueDeclare(queue: RabbitQueue,
                             durable: false,
                             exclusive: false,
                             autoDelete: false,
                             arguments: null);


        var body = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(newBid);

        // Publicer beskeden til køen
        _channel.BasicPublish(exchange: "",
                             routingKey: RabbitQueue,
                             mandatory: true,
                             basicProperties: null,
                             body: body);
        _logger.LogInformation("Booking sent to RabbitMQ");
        return newBid;
    }

    // Kontrollerer kataloget for et bud. Hvis budet opfylder visse kriterier, opdateres cache og database, og budet sendes til RabbitMQ.
    public async Task<bool> CheckCatalog(Bid bid)
    {
        _logger.LogInformation("ramt check catalog");
        DateTime timestamp = DateTime.UtcNow;
        DateTime timeinfive = timestamp.AddMinutes(5);
        Wrapper wrapper = new Wrapper();
        using (HttpClient client = new HttpClient())
        {
            client.BaseAddress = new Uri(CatalogHTTPBase);

            // Hent item og pris fra kataloget ved at foretage en GET-anmodning til Catalog-service
            HttpResponseMessage response = await client.GetAsync($"getitemandprice/{bid.CatalogId}");
            _logger.LogInformation("ramte lige db med den her:" + client.BaseAddress.ToString() + "getitemandprice/" + bid.CatalogId);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("fandt item i DB");
                string responseData = await response.Content.ReadAsStringAsync();
                wrapper = JsonConvert.DeserializeObject<Wrapper>(responseData);

                Console.WriteLine($"StartTime: {wrapper.StartTime}");
                Console.WriteLine($"EndTime: {wrapper.EndTime}");
                Console.WriteLine($"StartingPrice: {wrapper.StartingPrice}");
                Console.WriteLine($"BuyoutPrice: {wrapper.BuyoutPrice}");
            }
            else
            {
                _logger.LogInformation("fandt ikke noget i db" + response.StatusCode);
                return false;
            }
        }
        if (wrapper.StartingPrice > bid.BidValue || wrapper.StartTime > timestamp || wrapper.EndTime < timestamp)
        {
            _logger.LogInformation("bud er ikke gyldigt, har tjekket");
            throw new Exception("Bid did not meet criteria");
        }

        // Opdater tiden i cache og database med 5 minutter, hvis tiden snart er endtime
        if (wrapper.EndTime < timeinfive)
        {
            _logger.LogInformation("tiden er snart endtime");
            UpdateCache(bid.CatalogId, bid.BidValue, timeinfive);
            await UpdateDatabaseTime(bid.CatalogId, timeinfive);
        }
        else
        {
            _logger.LogInformation("updater cache");
            UpdateCache(bid.CatalogId, bid.BidValue, wrapper.EndTime);
        }

        // Send budet til RabbitMQ
        PostBid(bid);
        _logger.LogInformation("poster bid");

        return true;
    }

    // Kontrollerer cache for et bud. Hvis budet opfylder visse kriterier, opdateres cache og database, og budet sendes til RabbitMQ.
    public async Task<bool> CheckCache(Bid bid)
    {
        _logger.LogInformation("ramt check cache" + RedisConnection);
        DateTime timestamp = DateTime.UtcNow;
        DateTime timeinfive = timestamp.AddMinutes(5);
        double currentBid;
        DateTime endTime;

        // Opret forbindelse til Redis-cache ved hjælp af en ConnectionMultiplexer
        string redisConnectionString = RedisConnection;
        ConnectionMultiplexer redis = ConnectionMultiplexer.Connect(redisConnectionString);
        IDatabase cache = redis.GetDatabase();

        string priceKey = "price" + bid.CatalogId;
        string dateKey = "endtime" + bid.CatalogId;
        string lockKey = "lock" + bid.CatalogId;
        string lockValue = Guid.NewGuid().ToString();
        int lockExpirySeconds = 5;
        int retrycount = 6;
        bool acquiredLock = false;

        // Forsøg at tage en lås på cache
        for (int i = 0; i < retrycount; i++)
        {
            acquiredLock = cache.LockTake(lockKey, lockValue, TimeSpan.FromSeconds(lockExpirySeconds));
            _logger.LogInformation("har prøvet at tage log " + i + " gange" + acquiredLock);
            if (acquiredLock) { break; }
            await Task.Delay(TimeSpan.FromSeconds(1));
        }
        _logger.LogInformation("går ind i try med lock som " + acquiredLock);
        try
        {
            if (acquiredLock)
            {
                _logger.LogInformation("lock er gemt som true");
                var price = cache.StringGet(priceKey);
                var time = cache.StringGet(dateKey);
                _logger.LogInformation("har efterspugt ting i databasen " + price.ToString() + "  " + time.ToString());

                if (!price.IsNull && !time.IsNull)
                {
                    currentBid = double.Parse(price);
                    endTime = DateTime.Parse(time);
                    _logger.LogInformation("tingen i cachen er fundet");

                    if (currentBid >= bid.BidValue || endTime < timestamp)
                    {
                        _logger.LogInformation("budet er ikke gyldigt grundet data tjek");
                        throw new Exception("Bid did not meet criteria");
                    }

                    // Opdater tiden i cache og database med 5 minutter, hvis tiden snart er endtime
                    if (endTime < timeinfive)
                    {
                        _logger.LogInformation("tiden skal opdateres i cachen");
                        UpdateCache(bid.CatalogId, bid.BidValue, timeinfive);
                        await UpdateDatabaseTime(bid.CatalogId, timeinfive);
                    }
                    else
                    {
                        _logger.LogInformation("opdaterer cachen");
                        UpdateCache(bid.CatalogId, bid.BidValue);
                    }

                    // Send budet til RabbitMQ
                    PostBid(bid);
                    _logger.LogInformation("har postet bid");
                    return true;
                }
            }
            return false;
        }

        // Frigiver låsen
        finally
        {
            if (acquiredLock)
            {
                cache.LockRelease(lockKey, lockValue);
            }
        }
    }

    // Opdaterer cache med prisen og slutdatoen for et katalogelement
    public void UpdateCache(string catalogid, double price, DateTime endtime)
    {
        _logger.LogInformation("har ramt updatecache med tid på");
        string redisConnectionString = RedisConnection;
        ConnectionMultiplexer redis = ConnectionMultiplexer.Connect(redisConnectionString);
        IDatabase cache = redis.GetDatabase();

        cache.StringSet("price" + catalogid, price.ToString());
        cache.StringSet("endtime" + catalogid, endtime.ToString());
        _logger.LogInformation("har opdateret disse: " + "price" + catalogid + ":" + price.ToString());
        _logger.LogInformation("har opdateret disse: " + "endtime" + catalogid + ":" + endtime.ToString());
    }

    // Opdaterer cache med prisen for et katalogelement
    public void UpdateCache(string catalogid, double price)
    {
        _logger.LogInformation("har ramt updatecache uden tid");
        string redisConnectionString = RedisConnection;
        ConnectionMultiplexer redis = ConnectionMultiplexer.Connect(redisConnectionString);
        IDatabase cache = redis.GetDatabase();

        cache.StringSet("price" + catalogid, price.ToString());
        _logger.LogInformation("har opdateret disse: " + "price" + catalogid + ":" + price.ToString());
    }

    // Opdaterer slutdatoen for et katalogelement i databasen
    public async Task<bool> UpdateDatabaseTime(string catalogId, DateTime endTime)
    {
        using (HttpClient client = new HttpClient())
        {
            client.BaseAddress = new Uri(CatalogHTTPBase);
            TimeDTO timeDto = new TimeDTO(catalogId, endTime);
            string requestBody = JsonConvert.SerializeObject(timeDto);
            StringContent content = new StringContent(requestBody, Encoding.UTF8, "application/json");
            HttpResponseMessage response = await client.PutAsync("updatetime", content);
            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine("Update successful.");
                return true;
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                Console.WriteLine("Item not found.");
                throw new Exception("Could not reach catalogservice");
            }
            else
            {
                throw new Exception("Unknown Error");
            }
        }
    }

    // Henter Wrapper-objektet for et katalogelement fra databasen
    public async Task<Wrapper> FetchWrapper(string catalogid)
    {
        Wrapper wrapper = new Wrapper();
        using (HttpClient client = new HttpClient())
        {
            client.BaseAddress = new Uri(CatalogHTTPBase);

            HttpResponseMessage response = await client.GetAsync($"getitemandprice/{catalogid}");
            _logger.LogInformation("ramte lige db med den her:" + client.BaseAddress.ToString() + "getitemandprice/" + catalogid);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("fandt item i DB");
                string responseData = await response.Content.ReadAsStringAsync();
                wrapper = JsonConvert.DeserializeObject<Wrapper>(responseData);

                return wrapper;
            }
            else
            {
                _logger.LogInformation("fandt ikke noget i db" + response.StatusCode);
                throw new ItemsNotFoundException("Could not find item in db");
            }
        }
    }

    // Udfører en BuyOut for et bud ved at kontrollere betingelserne og opdatere databasen og cache'en.
    public async Task<bool> BuyOut(Bid buyoutbid)
    {
        Wrapper wrapper = new Wrapper();
        try
        {
            wrapper = await FetchWrapper(buyoutbid.CatalogId);
        }
        catch (Exception ex)
        {
            throw new Exception("could not fetch the buyoutprice from database");
        }

        if (buyoutbid.BidValue == wrapper.BuyoutPrice)
        {
            try
            {
                bool catalogpresent = false;
                bool cachepresent = await CheckCache(buyoutbid);
                if (!cachepresent)
                {
                    catalogpresent = await CheckCatalog(buyoutbid);
                }
                if (cachepresent || catalogpresent)
                {
                    UpdateCache(buyoutbid.CatalogId, buyoutbid.BidValue, DateTime.UtcNow);
                    await UpdateDatabaseTime(buyoutbid.CatalogId, DateTime.UtcNow);
                    return true;
                }
                else { throw new Exception("du prøvede at byde på noget der ikke eksiterede"); }
            }
            catch (Exception ex)
            {
                throw new Exception("Buyout did not meet buyout creteria " + ex.Message);
            }
        }
        else
        {
            throw new Exception("Buyout price didn't mach the requested buyuoutprice");
        }
    }
}
