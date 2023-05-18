using AuctionTrackerReceiver.Models;
using Microsoft.AspNetCore.Mvc;
using AuctionTrackerReceiver.Services;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
//using System.Web.Http;

namespace CatalogService.Controllers;


[ApiController]
[Route("bidreceiver/v1")]
public class CustomerController : ControllerBase
{


    private readonly ILogger<CustomerController> _logger;

    private readonly BiddingService _service;



    public CustomerController(ILogger<CustomerController> logger, BiddingService service)
    {
        _logger = logger;
        _service = service;
    }

     [HttpPost("newbid")]
    public async Task<IActionResult> CreateItem([FromBody] Bid data)
    {
        //burde return noget, men kan ikke fetche id
       
        try{
            
            bool insertedStatus = await _service.CheckCache(data);
            return Ok("Your bid was accepted");
        }    
        catch(Exception ex)
        {
            return BadRequest(ex.Message);
        }
        
       
        

        
    }

    
}