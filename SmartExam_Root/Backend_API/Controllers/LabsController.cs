using Backend_API.Data;
using Backend_API.Models;
using Backend_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Backend_API.Controllers;

/// <summary>
/// Module 9 — labs and workstations backing the seating map.
/// Teachers read them to assign seats; admins manage the inventory.
/// </summary>
[ApiController]
[Route("api/labs")]
[Authorize]
public class LabsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AuditService _audit;

    public LabsController(AppDbContext db, AuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    private Guid CurrentUserId =>
        Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value!);

    // GET /api/labs — labs with their workstations + who is on each machine (for seat pickers + Labs page)
    [HttpGet]
    [Authorize(Roles = "Teacher,Admin,SuperAdmin,Invigilator")]
    public async Task<IActionResult> GetAll()
    {
        var labs = await _db.Labs
            .Include(l => l.Workstations)
            .OrderBy(l => l.Name)
            .ToListAsync();

        // Match each machine to the most-recent student who logged in from it (by machine name).
        var bindings = await _db.DeviceBindings
            .Include(b => b.User)
            .Where(b => b.MachineName != null)
            .ToListAsync();
        var occupantByMachine = bindings
            .GroupBy(b => b.MachineName!)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(b => b.LastSeenAt).First());

        var result = labs.Select(l => new
        {
            l.LabId,
            l.Name,
            l.Location,
            Workstations = l.Workstations
                .OrderBy(w => w.MachineNumber)
                .Select(w =>
                {
                    occupantByMachine.TryGetValue(w.MachineNumber, out var occ);
                    return new
                    {
                        w.WorkstationId,
                        w.MachineNumber,
                        w.IpAddress,
                        OccupantName = occ?.User?.Name,
                        OccupantWindowsUser = occ?.WindowsUsername,
                        OccupantLastSeen = (DateTime?)occ?.LastSeenAt
                    };
                })
                .ToList()
        });

        return Ok(result);
    }

    // POST /api/labs — create a lab
    [HttpPost]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> CreateLab([FromBody] CreateLabRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return BadRequest(new { message = "Lab name is required." });

        var lab = new Lab { Name = req.Name.Trim(), Location = req.Location?.Trim() ?? string.Empty };
        _db.Labs.Add(lab);
        _audit.Add(CurrentUserId, "LAB_CREATED", "Lab", lab.LabId.ToString(), new { lab.Name });
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetAll), new { lab.LabId, lab.Name, lab.Location });
    }

    // POST /api/labs/{labId}/workstations — register a workstation (seat) with its IP
    [HttpPost("{labId:guid}/workstations")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> CreateWorkstation(Guid labId, [FromBody] CreateWorkstationRequest req)
    {
        var lab = await _db.Labs.FindAsync(labId);
        if (lab is null) return NotFound(new { message = "Lab not found." });
        if (string.IsNullOrWhiteSpace(req.MachineNumber))
            return BadRequest(new { message = "Machine number is required." });

        var duplicate = await _db.Workstations.AnyAsync(w => w.LabId == labId && w.MachineNumber == req.MachineNumber);
        if (duplicate) return Conflict(new { message = "A workstation with that machine number already exists in this lab." });

        var ws = new Workstation
        {
            LabId = labId,
            MachineNumber = req.MachineNumber.Trim(),
            IpAddress = req.IpAddress?.Trim() ?? string.Empty
        };
        _db.Workstations.Add(ws);
        _audit.Add(CurrentUserId, "WORKSTATION_CREATED", "Workstation", ws.WorkstationId.ToString(), new { lab.Name, ws.MachineNumber, ws.IpAddress });
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetAll), new { ws.WorkstationId, ws.MachineNumber, ws.IpAddress });
    }
}

public record CreateLabRequest(string Name, string? Location);
public record CreateWorkstationRequest(string MachineNumber, string? IpAddress);
