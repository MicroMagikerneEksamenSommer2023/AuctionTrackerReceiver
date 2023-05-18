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
using Enyim.Caching;
using Enyim.Caching.Configuration;
using Enyim.Caching.Memcached;

namespace AuctionTrackerReceiver.Services;

    public class BiddingService
    {
        private readonly ILogger<BiddingService> _logger;
        private readonly IConfiguration _config;
        private readonly IModel _channel;
        private readonly string RabbitHostName;
        private readonly string RabbitQueue;
        private readonly string CatalogHTTPBase;
        private readonly string MemCache;

        public BiddingService(ILogger<BiddingService> logger, IConfiguration config)
        {
            _config = config;
            _logger = logger;
            RabbitHostName = _config["rabbithostname"];
            RabbitQueue = _config["rabbitqueue"];
            CatalogHTTPBase = _config["cataloghttpbase"];
            MemCache = _config["memcache"];
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
            DateTime timestamp = DateTime.UtcNow;
            DateTime timeinfive = timestamp.AddMinutes(5);
            Wrapper wrapper = new Wrapper();
            using (HttpClient client = new HttpClient())
        {
            client.BaseAddress = new Uri(CatalogHTTPBase);

            HttpResponseMessage response = await client.GetAsync($"getitemandprice/{bid.CatalogId}");

            if (response.IsSuccessStatusCode)
            {
                string responseData = await response.Content.ReadAsStringAsync();
                wrapper = JsonConvert.DeserializeObject<Wrapper>(responseData);

                Console.WriteLine($"StartTime: {wrapper.StartTime}");
                Console.WriteLine($"EndTime: {wrapper.EndTime}");
                Console.WriteLine($"StartingPrice: {wrapper.StartingPrice}");
                Console.WriteLine($"BuyoutPrice: {wrapper.BuyoutPrice}");
            }
            else
            {
                throw new ItemsNotFoundException($"No item with ID {bid.CatalogId} was found in the database for the update.");
            }
        }
            if(wrapper.StartingPrice > bid.BidValue || wrapper.StartTime > timestamp || wrapper.EndTime < timestamp)
            {
                throw new Exception("Bid did not meet criteria");
            }
            
            if(wrapper.EndTime < timeinfive)
            {
                UpdateCache(bid.CatalogId,bid.BidValue,timeinfive);
                await UpdateDatabaseTime(bid.CatalogId,timeinfive);
            }
            else
            {
                UpdateCache(bid.CatalogId,bid.BidValue,wrapper.EndTime);
            }
            PostBid(bid);

           
            return true;
        }

        public async Task<bool> CheckCache(Bid bid)
        {
            DateTime timestamp = DateTime.UtcNow;
            DateTime timeinfive = timestamp.AddMinutes(5);
            double currentbid;
            DateTime endtime;

            MemcachedClientConfiguration config = new MemcachedClientConfiguration();
            config.AddServer(MemCache); // Replace with your Memcached server information
            MemcachedClient client = new MemcachedClient(config);

            string pricekey = "price" + bid.CatalogId;
            string datekey = "endtime" + bid.CatalogId;

            currentbid = client.Get<double>(pricekey);
            endtime = client.Get<DateTime>(datekey);

            if (currentbid != default(double) && endtime != default(DateTime))
            {

            if(currentbid > bid.BidValue || endtime < timestamp)
            {
                throw new Exception("Bid did not meet criteria");
            }
            
            if(endtime < timeinfive)
            {
                UpdateCache(bid.CatalogId,bid.BidValue,timeinfive);
                await UpdateDatabaseTime(bid.CatalogId,timeinfive);
            }
            else
            {
                UpdateCache(bid.CatalogId,bid.BidValue);
            }
            PostBid(bid);

            return true;
            }
            else{
                try
                {
                    return await CheckCatalog(bid);
                }
                catch{
                    throw new Exception("Catalog Call went bad");
                }
                    
            }
        
                
            
        }
        public void UpdateCache(string catalogid, double price, DateTime endtime)
        {
            
            MemcachedClientConfiguration config = new MemcachedClientConfiguration();
            config.AddServer(MemCache); // Replace with your Memcached server information
            MemcachedClient client = new MemcachedClient(config);

            client.Store(StoreMode.Set, "price" + catalogid,price);
            client.Store(StoreMode.Set, "endtime" + catalogid,endtime);

        }
        public void UpdateCache(string catalogid, double price)
        {
            
            MemcachedClientConfiguration config = new MemcachedClientConfiguration();
            config.AddServer(MemCache); // Replace with your Memcached server information
            MemcachedClient client = new MemcachedClient(config);

            client.Store(StoreMode.Set, "price" + catalogid,price);

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
