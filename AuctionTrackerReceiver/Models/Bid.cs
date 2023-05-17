using Newtonsoft.Json;


namespace AuctionTrackerReceiver.Models
{

    public class Bid
    {
        [Newtonsoft.Json.JsonProperty("catalogId")]
        public string CatalogId {get; set;}
        [Newtonsoft.Json.JsonProperty("buyerEmail")]
        public string BuyerEmail {get;set;}
        [Newtonsoft.Json.JsonProperty("BidValue")]
        public double BidValue {get;set;}
        
        [JsonConstructor]
        public Bid(string catalogid, string buyeremail, double bidvalue)
        {
            
            this.CatalogId = catalogid;
            this.BuyerEmail = buyeremail;
            this.BidValue = bidvalue;
        }


        

    }
}