using System;
namespace AuctionTrackerReceiver.Models{


public class ItemsNotFoundException : Exception
{
    public ItemsNotFoundException()
    {
    }

    public ItemsNotFoundException(string message)
        : base(message)
    {
    }

    public ItemsNotFoundException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
}