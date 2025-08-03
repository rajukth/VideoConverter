using Microsoft.AspNetCore.SignalR;

namespace VideoConverter.Hubs;

public class ProgressHub:Hub
{
    public async Task SendProgress(string message, int progress)
    {
        await Clients.All.SendAsync("UpdateProgress", new { message, progress });
    }
}