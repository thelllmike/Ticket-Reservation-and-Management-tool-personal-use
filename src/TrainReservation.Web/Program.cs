using TrainReservation.Data;
using TrainReservation.Data.Repositories;
using TrainReservation.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

// ---------------------------------------------------------------------------------------------
// Data layer.
//
// The store and every repository are singletons: the store holds all application state, so it has
// to outlive individual requests — a scoped repository would hand each request an empty database.
//
// The Services and Web layers only ever see the interfaces, so swapping these registrations for
// database-backed implementations is the whole of the work needed to add persistence later.
// ---------------------------------------------------------------------------------------------
builder.Services.AddSingleton<InMemoryDataStore>();
builder.Services.AddSingleton<IUserRepository, InMemoryUserRepository>();
builder.Services.AddSingleton<IStationRepository, InMemoryStationRepository>();
builder.Services.AddSingleton<IRouteRepository, InMemoryRouteRepository>();
builder.Services.AddSingleton<ITrainServiceRepository, InMemoryTrainServiceRepository>();
builder.Services.AddSingleton<IBookingRepository, InMemoryBookingRepository>();
builder.Services.AddSingleton<ISpecialRequestRepository, InMemorySpecialRequestRepository>();
builder.Services.AddSingleton<DataSeeder>();

// ---------------------------------------------------------------------------------------------
// Business logic. Stateless — all state lives in the store — so scoped is the natural lifetime.
// ---------------------------------------------------------------------------------------------
builder.Services.AddScoped<IScheduleService, ScheduleService>();
builder.Services.AddScoped<IBookingService, BookingService>();
builder.Services.AddScoped<ISpecialRequestService, SpecialRequestService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<IPredictionService, PredictionService>();

// The chat transcript is per-application, not per-request: one user, one conversation.
builder.Services.AddSingleton<TrainReservation.Web.Services.ChatTranscript>();

var app = builder.Build();

// Fill the in-memory store before the first request is served.
app.Services.GetRequiredService<DataSeeder>().Seed();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// No HTTPS redirection: the app is meant to run offline over plain http://localhost with a single
// `dotnet run`, and redirecting would demand a trusted dev certificate that may not be installed.
app.UseStaticFiles();
app.UseRouting();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
