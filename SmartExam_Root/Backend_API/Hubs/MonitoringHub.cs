using Backend_API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Backend_API.Hubs;

[Authorize(Roles = $"{nameof(SystemRole.OrganizationAdmin)},{nameof(SystemRole.Teacher)}")]
public class MonitoringHub : Hub
{
    public async Task JoinInstitution(Guid institutionId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, InstitutionGroup(institutionId));
    }

    public async Task LeaveInstitution(Guid institutionId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, InstitutionGroup(institutionId));
    }

    public static string InstitutionGroup(Guid institutionId) => $"institution:{institutionId}";
}