using System.IdentityModel.Tokens.Jwt;
using Condiva.Api.Common.Auth.Endpoints;
using Condiva.Api.Common.Errors;
using Condiva.Api.Common.Extensions;
using Condiva.Api.Common.Middleware;
using Condiva.Api.Features.Communities.Endpoints;
using Condiva.Api.Features.Events.Endpoints;
using Condiva.Api.Features.Items.Endpoints;
using Condiva.Api.Features.Loans.Endpoints;
using Condiva.Api.Features.Memberships.Endpoints;
using Condiva.Api.Features.Notifications.Endpoints;
using Condiva.Api.Features.Offers.Endpoints;
using Condiva.Api.Features.Reputations.Endpoints;
using Condiva.Api.Features.Requests.Endpoints;
using Condiva.Api.Features.Storage.Endpoints;
using Condiva.Api.Features.Users.Endpoints;
using Microsoft.IdentityModel.JsonWebTokens;

var builder = WebApplication.CreateBuilder(args);

// AuthLibrary Google validator reads raw JWT claim names (sub, email, nonce, ...).
// Disable inbound claim mapping globally to preserve original claim types.
JwtSecurityTokenHandler.DefaultMapInboundClaims = false;
JsonWebTokenHandler.DefaultMapInboundClaims = false;

builder.Services.AddCondivaServices(builder.Configuration);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        await ApiErrors.Internal().ExecuteAsync(context);
    });
});

app.UseSecurityHeaders();
app.UseCors("Frontend");

app.UseRateLimiter();
app.UseAuthentication();
app.UseCsrfProtection();
app.UseAuthorization();

app.MapAuthEndpoints();
app.MapUsersEndpoints();
app.MapCommunitiesEndpoints();
app.MapMembershipsEndpoints();
app.MapItemsEndpoints();
app.MapRequestsEndpoints();
app.MapOffersEndpoints();
app.MapLoansEndpoints();
app.MapEventsEndpoints();
app.MapNotificationsEndpoints();
app.MapReputationsEndpoints();
app.MapStorageEndpoints();
app.MapControllers();

app.Run();
