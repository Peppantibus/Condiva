using Condiva.Api.Common.Auth.Endpoints;
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

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCondivaServices(builder.Configuration);

var app = builder.Build();

var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins")
    .Get<string[]>()?
    .Any(origin => !string.IsNullOrWhiteSpace(origin)) == true
    || !string.IsNullOrWhiteSpace(builder.Configuration.GetValue<string>("AuthSettings:FrontendUrl"));

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseSecurityHeaders();

if (corsOrigins)
{
    app.UseCors("Frontend");
}

app.UseRateLimiter();
app.UseAuthentication();
app.UseCsrfProtection();
app.UseAuthorization();

app.MapAuthEndpoints();
app.MapCommunitiesEndpoints();
app.MapMembershipsEndpoints();
app.MapItemsEndpoints();
app.MapRequestsEndpoints();
app.MapOffersEndpoints();
app.MapLoansEndpoints();
app.MapEventsEndpoints();
app.MapNotificationsEndpoints();
app.MapReputationsEndpoints();
app.MapControllers();

app.Run();
