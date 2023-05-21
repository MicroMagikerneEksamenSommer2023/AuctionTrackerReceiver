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
                _logger.LogInformation("fandt ikke noget i db");
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
                UpdateCache(bid.CatalogId,bid.BidValue,wrapper.EndTime);
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
            double currentbid;
            DateTime endtime;

            string redisConnectionString = RedisConnection;
            ConnectionMultiplexer redis = ConnectionMultiplexer.Connect(redisConnectionString);
            IDatabase cache = redis.GetDatabase();

            string pricekey = "price" + bid.CatalogId;
            string datekey = "endtime" + bid.CatalogId;

            var price = cache.StringGet(pricekey);
            var time = cache.StringGet(datekey);

            if (!price.IsNull && !time.IsNull)
            {
                currentbid = Double.Parse(price);
                endtime = DateTime.Parse(time);
                _logger.LogInformation("tingen i cachen er fundet");
            if(currentbid > bid.BidValue || endtime < timestamp)
            {
                _logger.LogInformation("budet er ikke gyldigt grundet data tjek");
                throw new Exception("Bid did not meet criteria");
            }
            
            if(endtime < timeinfive)
            {
                _logger.LogInformation("tiden skal opdateres i cachen");
                UpdateCache(bid.CatalogId,bid.BidValue,timeinfive);
                await UpdateDatabaseTime(bid.CatalogId,timeinfive);
            }
            else
            {
                _logger.LogInformation("opdaterer cachen");
                UpdateCache(bid.CatalogId,bid.BidValue);
            }
            PostBid(bid);
            _logger.LogInformation("har postet bid");
            return true;
            }
            else
            {
                return false;     
            }
        
                
            
        }
        public void UpdateCache(string catalogid, double price, DateTime endtime)
        {
            
            string redisConnectionString = RedisConnection;
            ConnectionMultiplexer redis = ConnectionMultiplexer.Connect(redisConnectionString);
            IDatabase cache = redis.GetDatabase();

            cache.StringSet("price" + catalogid,price.ToString());
            cache.StringSet("endtime" + catalogid,endtime.ToString());

        }
        public void UpdateCache(string catalogid, double price)
        {
            
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
    }
