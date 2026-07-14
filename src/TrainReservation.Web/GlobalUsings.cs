// The domain entity is called Route, exactly as the brief specifies, but ASP.NET Core also ships a
// Microsoft.AspNetCore.Routing.Route. Inside the Web project both namespaces are in scope, so an
// unqualified "Route" is ambiguous.
//
// Aliasing it once here — rather than renaming the entity or qualifying it at every use — keeps the
// domain model matching the brief and the controllers and views readable. Razor views compile into
// this same assembly, so they pick the alias up too.
global using Route = TrainReservation.Domain.Route;
