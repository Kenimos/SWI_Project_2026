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

builder.Services.AddSingleton<IClock, SystemClock>();

// v0.2 - délka schvalovacího okna (REQ-08) je v docs/specification.md výslovně
// TBD; "Reservations:ApprovalWindowHours" v appsettings.json je jen demonstrační
// placeholder (default 24h), ne odsouhlasené business pravidlo.
var approvalWindow = TimeSpan.FromHours(builder.Configuration.GetValue("Reservations:ApprovalWindowHours", 24.0));
builder.Services.AddScoped(sp =>
    new ReservationService(sp.GetRequiredService<ParkingDbContext>(), sp.GetRequiredService<IClock>(), approvalWindow));

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

// OP-03 — Confirm Reservation (docs/specification.md)
app.MapPost("/reservations/{id:int}/confirm", async (int id, ConfirmReservationRequest request, ReservationService service) =>
{
    var result = await service.ConfirmReservationAsync(id, request.UserId);

    if (!result.Success)
    {
        return result.Error switch
        {
            ConfirmReservationError.NotFound => Results.NotFound(new { error = "reservation_not_found" }),
            ConfirmReservationError.Unauthorized => Results.Json(new { error = "unauthorized" }, statusCode: 401),
            ConfirmReservationError.InvalidState => Results.Json(new { error = "invalid_state" }, statusCode: 409),
            ConfirmReservationError.TooLate => Results.Json(new { error = "too_late" }, statusCode: 409),
            ConfirmReservationError.SpotInactive => Results.Json(new { error = "spot_inactive" }, statusCode: 409),
            ConfirmReservationError.Overlap => Results.Json(new { error = "overlap" }, statusCode: 409),
            _ => Results.Json(new { error = "unknown" }, statusCode: 400)
        };
    }

    return Results.Ok(new { id, state = result.State!.Value.ToString() });
})
.WithName("ConfirmReservation")
.WithOpenApi();

// OP-04 — Cancel Reservation (docs/specification.md)
app.MapPost("/reservations/{id:int}/cancel", async (int id, CancelReservationRequest request, ReservationService service) =>
{
    var result = await service.CancelReservationAsync(id, request.UserId);

    if (!result.Success)
    {
        return result.Error switch
        {
            CancelReservationError.NotFound => Results.NotFound(new { error = "reservation_not_found" }),
            CancelReservationError.Unauthorized => Results.Json(new { error = "unauthorized" }, statusCode: 401),
            CancelReservationError.TooLate => Results.Json(new { error = "too_late" }, statusCode: 409),
            CancelReservationError.InvalidState => Results.Json(new { error = "invalid_state" }, statusCode: 409),
            _ => Results.Json(new { error = "unknown" }, statusCode: 400)
        };
    }

    return Results.Ok(new { id, state = result.State!.Value.ToString() });
})
.WithName("CancelReservation")
.WithOpenApi();

// OP-05 — Approve Reservation: approve-větev (docs/specification.md, v0.2)
app.MapPost("/reservations/{id:int}/approve", async (int id, ApproveReservationRequest request, ReservationService service) =>
{
    var result = await service.ApproveReservationAsync(id, request.ApproverUserId);

    if (!result.Success)
    {
        return result.Error switch
        {
            ApproveReservationError.NotFound => Results.NotFound(new { error = "reservation_not_found" }),
            ApproveReservationError.Unauthorized => Results.Json(new { error = "unauthorized" }, statusCode: 401),
            ApproveReservationError.InvalidState => Results.Json(new { error = "invalid_state" }, statusCode: 409),
            ApproveReservationError.Expired => Results.Json(new { error = "expired" }, statusCode: 409),
            ApproveReservationError.TooLate => Results.Json(new { error = "too_late" }, statusCode: 409),
            ApproveReservationError.SpotInactive => Results.Json(new { error = "spot_inactive" }, statusCode: 409),
            ApproveReservationError.Overlap => Results.Json(new { error = "overlap" }, statusCode: 409),
            _ => Results.Json(new { error = "unknown" }, statusCode: 400)
        };
    }

    return Results.Ok(new { id, state = result.State!.Value.ToString() });
})
.WithName("ApproveReservation")
.WithOpenApi();

// OP-05 — Approve Reservation: reject-větev (docs/specification.md, v0.2)
app.MapPost("/reservations/{id:int}/reject", async (int id, RejectReservationRequest request, ReservationService service) =>
{
    var result = await service.RejectReservationAsync(id, request.ApproverUserId);

    if (!result.Success)
    {
        return result.Error switch
        {
            RejectReservationError.NotFound => Results.NotFound(new { error = "reservation_not_found" }),
            RejectReservationError.Unauthorized => Results.Json(new { error = "unauthorized" }, statusCode: 401),
            RejectReservationError.InvalidState => Results.Json(new { error = "invalid_state" }, statusCode: 409),
            RejectReservationError.Expired => Results.Json(new { error = "expired" }, statusCode: 409),
            _ => Results.Json(new { error = "unknown" }, statusCode: 400)
        };
    }

    return Results.Ok(new { id, state = result.State!.Value.ToString() });
})
.WithName("RejectReservation")
.WithOpenApi();

app.Run();

// Pro xUnit testy přes WebApplicationFactory<Program>.
public partial class Program { }
