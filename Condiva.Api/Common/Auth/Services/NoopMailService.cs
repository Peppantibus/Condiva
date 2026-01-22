using System.Threading.Tasks;
using AuthLibrary.Interfaces;
using AuthLibrary.Models.Dto;

namespace Condiva.Api.Common.Auth.Services;

public sealed class NoopMailService : IMailService
{
    public Task SendAsync(MailDto mail)
    {
        return Task.CompletedTask;
    }
}
