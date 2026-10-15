using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.TicketValidations;
using SmartBus.Api.Entities;
using SmartBus.Api.Services;
using UserEntity = SmartBus.Api.Entities.User;

namespace SmartBus.Tests;

/// <summary>
/// Chạy trên app THẬT (TestAppFactory) — ghim dòng đăng ký DI của <c>ITicketValidationService</c>
/// trong Program.cs: xoá dòng AddScoped là test này đỏ ngay, không phải chờ tới lúc mở endpoint mới
/// biết. Khoá giả rõ ràng của test — không phải khoá thật (luật 2), cùng lối TicketQrWiringTests.
/// </summary>
public class TicketValidationWiringTests
{
    [Fact]
    public void App_that_resolve_duoc_ITicketValidationService()
    {
        using var factory = NewFactory();

        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ITicketValidationService>();

        Assert.IsType<TicketValidationService>(service);
    }

    [Fact]
    public async Task App_that_soat_duoc_mot_ma_that_qua_service()
    {
        using var factory = NewFactory();

        // Đi trọn một vòng đời qua cấu hình thật của app: ký mã → seed vé → gọi ValidateAsync → Valid.
        var passenger = new UserEntity
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "0911111111",
            FullName = "Khách Đi Thử",
            PasswordHash = "hash",
            IsActive = true,
        };

        var trip = new Trip
        {
            Id = Guid.NewGuid(),
            RouteId = Guid.NewGuid(),
            BusId = Guid.NewGuid(),
            DepartureTime = DateTime.UtcNow.AddDays(1),
            Status = TripStatus.Scheduled,
        };

        var seat = new Seat
        {
            Id = Guid.NewGuid(),
            BusId = Guid.NewGuid(),
            SeatLayoutId = Guid.NewGuid(),
            SeatNumber = "C7",
        };

        var ticket = new Ticket
        {
            Id = Guid.NewGuid(),
            Code = string.Empty,
            TripId = trip.Id,
            SeatId = seat.Id,
            UserId = passenger.Id,
            Price = 12_000,
        };

        await factory.SeedAsync(db =>
        {
            db.Users.Add(passenger);
            db.Trips.Add(trip);
            db.Seats.Add(seat);
            db.Tickets.Add(ticket);
        });

        using var scope = factory.Services.CreateScope();
        var qr = scope.ServiceProvider.GetRequiredService<ITicketQrService>();
        var validation = scope.ServiceProvider.GetRequiredService<ITicketValidationService>();

        var code = qr.GenerateCode(ticket.Id);

        var result = await validation.ValidateAsync(
            new ValidateTicketRequest { Code = code },
            validatedByUserId: Guid.NewGuid());

        Assert.True(result.Success);
        Assert.True(result.Data!.Valid);
        Assert.Null(result.Data.Reason);
        Assert.Equal(ticket.Id, result.Data.Ticket!.Id);
    }

    private static TicketValidationTestFactory NewFactory() => new();

}
