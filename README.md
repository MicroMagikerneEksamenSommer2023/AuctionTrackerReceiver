# Auction Tracker Receiver API Dokumentation

Det nedenstående dokument beskriver de tilgængelige endpoints og deres funktionalitet i Auction Tracker Receiver API.

## Endpoints

### Oprettelse af et nyt bud

- URL: `POST /bidreceiver/v1/newbid`
- Beskrivelse: Denne endpoint bruges til at oprette et nyt bud.
- Anmodningens krop: JSON-data, der repræsenterer budet.
  - Egenskaber:
    - `BidId` (string): Unik identifikator for budet.
    - `ItemId` (string): Unik identifikator for varen, der bydes på.
    - `BidAmount` (number): Beløbet for budet.
    - `BidTime` (string): Tidspunktet for budet i ISO 8601-format.
- Svar:
  - Succes: HTTP-status 200 OK. Budet blev accepteret.
  - Fejl: HTTP-status 400 Bad Request. Der opstod en fejl under oprettelsen af budet.

### Køb af et item med buyout

- URL: `POST /bidreceiver/v1/buyout`
- Beskrivelse: Denne endpoint bruges til at købe et item med buyout-prisen.
- Anmodningens krop: JSON-data, der repræsenterer buyout-oplysningerne.
  - Egenskaber:
    - `BidId` (string): Unik identifikator for købet.
    - `ItemId` (string): Unik identifikator for det item, der købes.
    - `BidAmount` (number): Beløbet for købet.
    - `BidTime` (string): Tidspunktet for købet i ISO 8601-format.
- Svar:
  - Succes: HTTP-status 200 OK. Købet blev accepteret.
  - Fejl: HTTP-status 400 Bad Request. Der opstod en fejl under købet.

## CustomerController-klasse

Klassen `CustomerController` repræsenterer controlleren, der håndterer anmodninger til Auction Tracker Receiver.

### Konstruktør

- `CustomerController(ILogger<CustomerController> logger, BiddingService service)`: Opretter en ny instans af `CustomerController`-klassen med den angivne logger og BiddingService.

### Metoder

- `CreateItem([FromBody] Bid data)`: Opretter et nyt bud baseret på de modtagne data.
- `BuyOut([FromBody] Bid data)`: Udfører et køb af et item baseret på de modtagne buyout-data.


# Dokumentation for AuctionTrackerReciver Service 

Dette dokument beskriver de tilhørende klasser og deres funktionalitet i Auction Tracker Receiver.

## Bid-klasse

Klassen `Bid` repræsenterer et bud i auktionsprocessen. Den bruges til at overføre budoplysninger, herunder katalog-id, køberens e-mail-adresse og budværdien.

### Egenskaber

- `CatalogId` (string): Unik identifikator for kataloget.
- `BuyerEmail` (string): E-mail-adressen for budgiveren.
- `BidValue` (number): Værdien af budet.

### Constructor

- `Bid(string catalogId, string buyerEmail, double bidValue)`: Opretter en ny instans af `Bid`-klassen med de angivne værdier.

## Wrapper-klasse

Klassen `Wrapper` repræsenterer en wrapper for auktionsoplysninger. Den bruges til at pakke auktionsdata, herunder starttidspunkt, sluttidspunkt, startpris og buyout-pris.

### Egenskaber

- `StartTime` (DateTime): Starttidspunktet for auktionen.
- `EndTime` (DateTime): Sluttidspunktet for auktionen.
- `StartingPrice` (number): Startprisen for auktionen.
- `BuyoutPrice` (number): Buyout-prisen for auktionen.

### Constructor

- `Wrapper(DateTime startTime, DateTime endTime, double startingPrice, double buyoutPrice)`: Opretter en ny instans af `Wrapper`-klassen med de angivne værdier.

## CustomException-klasse - ItemsNotFoundException

Klassen `CustomException` repræsenterer en exception, der kastes, når der ikke findes nogen varer. Den bruges til at signalere, at der ikke findes nogen varer i auktionskataloget.

### Constructor

- `ItemsNotFoundException()`: Opretter en ny instans af `ItemsNotFoundException`-klassen uden en specifik fejlmeddelelse.
- `ItemsNotFoundException(string message)`: Opretter en ny instans af `CustomException`-klassen med den angivne fejlmeddelelse.
- `ItemsNotFoundException(string message, Exception inner)`: Opretter en ny instans af `CustomException`-klassen med den angivne fejlmeddelelse og indre exception.

## TimeDTO-klasse

Klassen `TimeDTO` repræsenterer tidspunktet for en auktion. Den bruges til at overføre tidspunktet for en auktion, herunder katalog-id og sluttidspunkt.

### Egenskaber

- `CatalogId` (string): Unik identifikator for kataloget.
- `EndTime` (DateTime): Sluttidspunktet for auktionen.

### Constructor

- `TimeDTO(string catalogId, DateTime endTime)`: Opretter en ny instans af `TimeDTO`-klassen med de angivne værdier.
