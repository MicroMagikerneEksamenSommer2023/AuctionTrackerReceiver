using Newtonsoft.Json;


namespace AuctionTrackerReceiver.Models
{

    public class Wrapper
    {
        [Newtonsoft.Json.JsonProperty("startTime")]
        public DateTime StartTime {get; set;}
        [Newtonsoft.Json.JsonProperty("endTime")]
        public DateTime EndTime {get;set;}
        [Newtonsoft.Json.JsonProperty("startingPrice")]
        public double StartingPrice {get;set;}
        [Newtonsoft.Json.JsonProperty("buyoutPrice")]
        public double BuyoutPrice {get;set;}
        
        [JsonConstructor]
        public Wrapper(DateTime starttime, DateTime endtime, double startingprice, double buyoutprice)
        {
         this.StartTime = starttime;
         this.EndTime = endtime;
         this.StartingPrice = startingprice;
         this.BuyoutPrice = buyoutprice;   
        }
        public Wrapper()
        {}


        

    }
}