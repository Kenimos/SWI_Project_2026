using Microsoft.EntityFrameworkCore;
using ParkingReservation.Api.Contracts;
using ParkingReservation.Api.Data;
using ParkingReservation.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// SQLite databáze; connection string je v appsettings.json (ConnectionStrings:DefaultConnection).
builder.Services.AddDbContext<ParkingDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<ReservationService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// OP-01 — Create Reservation (docs/specification.md)
app.MapPost("/reservations", async (CreateReservationRequest request, ReservationService service) =>
{
    var result = await service.CreateReservationAsync(request);

    if (!result.Success)
    {
        return result.Error switch
        {
            CreateReservationError.Unauthorized => Results.Json(new { error = "unauthorized" }, statusCode: 401),
            CreateReservationError.SpotNotFound => Results.Json(new { error = "spot_not_found" }, statusCode: 404),
            CreateReservationError.InvalidInterval => Results.Json(new { error = "invalid_interval" }, statusCode: 400),
            CreateReservationError.SpotRequiresEligibility => Results.Json(new { error = "spot_requires_eligibility" }, statusCode: 400),
            _ => Results.Json(new { error = "unknown" }, statusCode: 400)
        };
    }

    return Results.Created($"/reservations/{result.ReservationId}", new { id = result.ReservationId, state = result.State!.Value.ToString() });
})
.WithName("CreateReservation")
.WithOpenApi();

// OP-02 — Check Availability (docs/specification.md)
app.MapGet("/parking-spots/{spotId:int}/availability", async (int spotId, DateTime start, DateTime end, ReservationService service) =>
{
    var result = await service.CheckAvailabilityAsync(spotId, start, end);

    if (!result.Success)
    {
        return result.Error switch
        {
            AvailabilityError.SpotNotFound => Results.Json(new { error = "spot_not_found" }, statusCode: 404),
            AvailabilityError.InvalidInterval => Results.Json(new { error = "invalid_interval" }, statusCode: 400),
            _ => Results.Json(new { error = "unknown" }, statusCode: 400)
        };
    }

    return Results.Ok(new { available = result.Available });
})
.WithName("CheckAvailability")
.WithOpenApi();

app.Run();

// Pro xUnit testy přes WebApplicationFactory<Program>.
public partial class Program { }
