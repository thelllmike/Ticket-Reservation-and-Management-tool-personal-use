namespace TrainReservation.Domain;

/// <summary>Lifecycle of a booking.</summary>
public enum BookingStatus
{
    Pending,
    Confirmed,
    Cancelled,
    Completed
}

/// <summary>How often a recurring booking repeats.</summary>
public enum Frequency
{
    Daily,
    Weekly,
    Monthly
}

/// <summary>Class of travel; drives the seat price multiplier.</summary>
public enum TravelClass
{
    Standard,
    FirstClass
}

/// <summary>Categories of assistance a traveller can request.</summary>
public enum RequestType
{
    WheelchairAccess,
    Meal,
    ExtraLuggage,
    QuietCoach,
    BicycleSpace
}

/// <summary>Lifecycle of a special request.</summary>
public enum RequestStatus
{
    Requested,
    Fulfilled,
    Declined
}
