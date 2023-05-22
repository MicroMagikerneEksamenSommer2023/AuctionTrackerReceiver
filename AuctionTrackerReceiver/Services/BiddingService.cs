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

    public class BiddingService
    {
        private readonly ILogger<BiddingService> _logger;
        private readonly IConfiguration _config;
        private readonly IModel _channel;
        private readonly string RabbitHostName;
        private readonly string RabbitQueue;
        private readonly string CatalogHTTPBase;
        private readonly string RedisConnection;

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

        public async Task<bool> CheckCatalog(Bid bid)
        {
            _logger.LogInformation("ramp check catalog");
            DateTime timestamp = DateTime.UtcNow;
            DateTime timeinfive = timestamp.AddMinutes(5);
            Wrapper wrapper = new Wrapper();
            using (HttpClient client = new HttpClient())
        {
            client.BaseAddress = new Uri(CatalogHTTPBase);

            HttpResponseMessage response = await client.GetAsync($"getitemandprice/{bid.CatalogId}");
            _logger.LogInformation("ramte lige db med den her:" + client.BaseAddress.ToString() + "getitemandprice/" + bid.CatalogId );

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
            if(wrapper.StartingPrice > bid.BidValue || wrapper.StartTime > timestamp || wrapper.EndTime < timestamp)
            {
                _logger.LogInformation("bud er ikke gyldigt, har tjekket");
                throw new Exception("Bid did not meet criteria");
            }
            
            if(wrapper.EndTime < timeinfive)
            {
                _logger.LogInformation("tiden er snart endtime");
                UpdateCache(bid.CatalogId,bid.BidValue,timeinfive);
                await UpdateDatabaseTime(bid.CatalogId,timeinfive);
            }
            else
            {
                _logger.LogInformation("updater cache");
                UpdateCache(bid.CatalogId,bid.BidValue);
            }
            PostBid(bid);
            _logger.LogInformation("poster bid");

           
            return true;
        }

       public async Task<bool> CheckCache(Bid bid)
{
    _logger.LogInformation("ramt check cache" + RedisConnection);
    DateTime timestamp = DateTime.UtcNow;
    DateTime timeinfive = timestamp.AddMinutes(5);
    double currentBid;
    DateTime endTime;

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
    for(int i = 0;i<retrycount;i++)
    {
        acquiredLock = cache.LockTake(lockKey, lockValue, TimeSpan.FromSeconds(lockExpirySeconds));
        _logger.LogInformation("har prøvet at tage log " + i + " gange" + acquiredLock);
        if(acquiredLock){break;}
        await Task.Delay(TimeSpan.FromSeconds(1));
    }
    _logger.LogInformation("går ind i try med lock som " + acquiredLock);
    try
    {
        if (acquiredLock)
        {
            _logger.LogInformation("lock er gamt som true");
            var price = cache.StringGet(priceKey);
            var time = cache.StringGet(dateKey);
            _logger.LogInformation("har efterspugt ting i databasen");

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
                PostBid(bid);
                _logger.LogInformation("har postet bid");
                return true;
            }
        }

        return false;
    }
    finally
    {
        if (acquiredLock)
        {
            cache.LockRelease(lockKey, lockValue);
        }
    }
}
        public void UpdateCache(string catalogid, double price, DateTime endtime)
        {
            _logger.LogInformation("har ramt updatecache med tid på");
            string redisConnectionString = RedisConnection;
            ConnectionMultiplexer redis = ConnectionMultiplexer.Connect(redisConnectionString);
            IDatabase cache = redis.GetDatabase();

            cache.StringSet("price" + catalogid,price.ToString());
            cache.StringSet("endtime" + catalogid,endtime.ToString());

        }
        public void UpdateCache(string catalogid, double price)
        {
            _logger.LogInformation("har ramt updatecache uden tid");
            string redisConnectionString = RedisConnection;
            ConnectionMultiplexer redis = ConnectionMultiplexer.Connect(redisConnectionString);
            IDatabase cache = redis.GetDatabase();

            cache.StringSet("price" + catalogid,price.ToString());

        }
        public async Task<bool> UpdateDatabaseTime(string catalogid, DateTime endtime)
        {
            using (HttpClient client = new HttpClient())
        {
            // Set the base URL of the API endpoint
            client.BaseAddress = new Uri(CatalogHTTPBase);

            // Create a new StringContent with the request body
            string requestBody = $"\"{catalogid}\", \"{endtime.ToString("yyyy-MM-ddTHH:mm:ss")}\"";
            StringContent content = new StringContent(requestBody, System.Text.Encoding.UTF8, "application/json");

            // Send the PUT request and receive the response
            HttpResponseMessage response = await client.PutAsync("updatetime", content);

            // Ensure the request was successful
            if (response.IsSuccessStatusCode)
            {
                // Process the successful response
                Console.WriteLine("Update successful.");
                return true;
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // Handle the case when the item is not found
                Console.WriteLine("Item not found.");
                throw new Exception("Could not reach catalogservice");
            }
            else
            {
                throw new Exception("Unknow Error");
            }
        }
        }
        public async Task<Wrapper> FetchWrapper(string catalogid)
        {
            Wrapper wrapper = new Wrapper();
            using (HttpClient client = new HttpClient())
        {
            client.BaseAddress = new Uri(CatalogHTTPBase);

            HttpResponseMessage response = await client.GetAsync($"getitemandprice/{catalogid}");
            _logger.LogInformation("ramte lige db med den her:" + client.BaseAddress.ToString() + "getitemandprice/" + catalogid );

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
            
            if(buyoutbid.BidValue == wrapper.BuyoutPrice)
            {
                try
                {
                    bool cachepresent = await CheckCache(buyoutbid);
                    if (!cachepresent)
                    {
                        bool catalogpresent = await CheckCatalog(buyoutbid);
                    }
                    UpdateCache(buyoutbid.CatalogId,buyoutbid.BidValue,DateTime.UtcNow);
                    await UpdateDatabaseTime(buyoutbid.CatalogId,DateTime.UtcNow);
                    return true;
                }    
                catch(Exception ex)
                {
                    throw new Exception("Buyout did not meet buyout creteria " + ex.Message);
                } 
            }
            else{
                throw new Exception("Buyout price didn't mach the requested buyuoutprice");
            }

            
        }
    }
