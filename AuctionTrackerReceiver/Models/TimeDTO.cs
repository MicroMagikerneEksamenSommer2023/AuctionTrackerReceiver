using Newtonsoft.Json;


namespace AuctionTrackerReceiver.Models
{

    public class TimeDTO
    {
        [Newtonsoft.Json.JsonProperty("catalogId")]
        public string CatalogId {get; set;}
        [Newtonsoft.Json.JsonProperty("endTime")]
        public DateTime EndTime {get;set;}
        
        [JsonConstructor]
        public TimeDTO(string catalogId, DateTime endTime)
        {
         this.CatalogId = catalogId;
         this.EndTime = endTime;
     
        }
    }
}