using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Moq;
using AuctionTrackerReceiver.Controllers;
using AuctionTrackerReceiver.Services;
using Microsoft.AspNetCore.Mvc;
using AuctionTrackerReceiver.Models;

namespace AuctionTrackerReceiver.Tests;

public class ServiceTests
{
    // Attributter til ILogger og IConfuguration
    private ILogger<AuctionController> _logger = null;
    private IConfiguration _configuration = null;

    // Opsætter testmiljøet ved at initialisere _logger og _configuration
    [SetUp]
    public void Setup()
    {
        _logger = new Mock<ILogger<AuctionController>>().Object;

        var myConfiguration = new Dictionary<string, string?>
        {
            {"AuctionTrackerReceiverBrokerHost", "http://testhost.local"}
        };

        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(myConfiguration)
            .Build();
    }

    // Tester oprettelse af et bid gennem catalog med succes
    [Test]
    public async Task CreateBidCatalogTest_Succes()
    {
        //Arrange
        var bid = CreateBid("45", "kerstine@live.dk", 5000);
        bool bidTrue = true;
        bool bidFalse = false; 

        var stubService = new Mock<IBiddingService>();

        stubService.Setup(svc => svc.CheckCatalog(bid))
            .Returns(Task.FromResult<bool>(bidTrue));

        stubService.Setup(svc => svc.CheckCache(bid))
            .Returns(Task.FromResult<bool>(bidFalse));

        var controller = new AuctionController(_logger,_configuration, stubService.Object);
 
        //Act
        var result = await controller.CreateBid(bid);

        //Assert
        Assert.That(result, Is.TypeOf<OkObjectResult>()); 
    }

    // Tester oprettelse af et bid gennem cache med succes
    [Test]
    public async Task CreateBidCacheTest_Succes()
    {
        //Arrange
        var bid = CreateBid("45", "kerstine@live.dk", 5000);
        bool bidTrue = true;
       
        var stubService = new Mock<IBiddingService>();

        stubService.Setup(svc => svc.CheckCache(bid))
            .Returns(Task.FromResult<bool>(bidTrue));

        var controller = new AuctionController(_logger,_configuration, stubService.Object);
 
        //Act
        var result = await controller.CreateBid(bid);

        //Assert
        Assert.That(result, Is.TypeOf<OkObjectResult>()); 
    }

    // Tester oprettelse af et bid med fejl
    [Test]
    public async Task CreateBidTest_Failure()
    {
        //Arrange
        var bid = CreateBid("45", "kerstine@live.dk", 5000);
        bool bidFalse = false; 

        var stubService = new Mock<IBiddingService>();

        stubService.Setup(svc => svc.CheckCatalog(bid))
            .Returns(Task.FromResult<bool>(bidFalse));

        stubService.Setup(svc => svc.CheckCache(bid))
            .Returns(Task.FromResult<bool>(bidFalse));

        var controller = new AuctionController(_logger,_configuration, stubService.Object);
 
        //Act
        var result = await controller.CreateBid(bid);

        //Assert
        Assert.That(result, Is.TypeOf<BadRequestObjectResult>()); 
    }

    /// <summary>
    /// Helper method for creating Bid instance.
    /// </summary>
    /// <returns></returns>
    private Bid CreateBid(string catalogId, string buyerEmail, double bidValue)
    {
        var bid = new Bid(catalogId, buyerEmail, bidValue);
       
        return bid; 
     }




}