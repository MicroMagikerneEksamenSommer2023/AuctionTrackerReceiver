using AuctionTrackerReceiver.Models;
using Microsoft.AspNetCore.Mvc;
using AuctionTrackerReceiver.Services;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
//using System.Web.Http;
namespace AuctionTrackerReceiver.Controllers;


[ApiController]
[Route("bidreceiver/v1")]
public class AuctionController : ControllerBase
{


    private readonly ILogger<AuctionController> _logger;

    private readonly IBiddingService _service;



    public AuctionController(ILogger<AuctionController> logger, IConfiguration configuration, IBiddingService service)
    {
        _logger = logger;
        _service = service;
    }

    [HttpPost("newbid")]
    public async Task<IActionResult> CreateBid([FromBody] Bid data)
    {
        
        try{
            bool catalogpresent = false;
            bool cachepresent = await _service.CheckCache(data);
            _logger.LogInformation("har tjekket cache, status" + cachepresent);
            if (!cachepresent)
            {
                catalogpresent = await _service.CheckCatalog(data);
                _logger.LogInformation("har tjekket catalog, status" + cachepresent);
            }
            if(catalogpresent || cachepresent){
                return Ok("Your bid was accepted");
            }
            else{
                return BadRequest("Du prøver at byde på et item der ikek findes homie");
            }
        }    
        catch(Exception ex)
        {
            return BadRequest(ex.Message);
        }   
    }
    [HttpPost("buyout")]
    public async Task<IActionResult> BuyOut([FromBody] Bid data)
    {
        try
        {
            bool buyoutcheck = await _service.BuyOut(data);
            return Ok("Your buyout was acceted");
        }
        catch (Exception ex)
        {
            
            return BadRequest(ex.Message);
        }
    }
    
}