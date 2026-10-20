using System.Net;
using Backend_API.Services;
using FluentAssertions;

namespace Backend_API.Tests.Tests.Eligibility;

/// <summary>
/// M9-U-04…06 — the pure Module 9 rules behind the endpoints: attendance CSV parsing,
/// attendance → eligibility, and seat matching (TEST_CASES.md §M9, layer U).
/// </summary>
public class EligibilityRulesTests
{
    // ── M9-U-04 attendance CSV parser ─────────────────────────────────────────

    [Fact]
    public void M9_U_04_Csv_ColumnsMatchedByHeaderName_InAnyOrder()
    {
        var a = AttendanceCsvParser.Parse("email,total,attended\na@x.com,40,30\n");
        var b = AttendanceCsvParser.Parse("Attended,EMAIL,Total\n30,a@x.com,40\n");

        foreach (var r in new[] { a, b })
        {
            r.HeaderError.Should().BeNull();
            r.Rows.Should().ContainSingle().Which.Should().Be(new AttendanceCsvRow(2, "a@x.com", 40, 30, null));
        }
    }

    [Fact]
    public void M9_U_04_Csv_HandlesBomCrlfBlankLinesQuotesAndSemicolons()
    {
        var r = AttendanceCsvParser.Parse("﻿email;total;attended\r\n\r\n\"b@x.com\";\"10\";5\r\n");
        r.HeaderError.Should().BeNull();
        r.Rows.Should().ContainSingle().Which.Should().Be(new AttendanceCsvRow(3, "b@x.com", 10, 5, null));

        var quoted = AttendanceCsvParser.Parse("name,email,total,attended\n\"Khan, Sara\",s@x.com,4,4\n");
        quoted.Rows.Single().Should().Match<AttendanceCsvRow>(x => x.Email == "s@x.com" && x.Error == null);
    }

    [Theory]
    [InlineData("a@x.com,ten,5", "whole numbers")]
    [InlineData("a@x.com,10,-1", "negative")]
    [InlineData("a@x.com,10,11", "greater than total")]
    [InlineData(",10,5", "missing email")]
    [InlineData("a@x.com,20000,5", "larger than")]
    public void M9_U_04_Csv_InvalidRowsCarryAReason(string line, string reason)
    {
        var row = AttendanceCsvParser.Parse("email,total,attended\n" + line).Rows.Single();
        row.Error.Should().Contain(reason);
    }

    [Fact]
    public void M9_U_04_Csv_DuplicateEmailAndMissingHeader()
    {
        var dup = AttendanceCsvParser.Parse("email,total,attended\na@x.com,10,5\nA@X.com,10,6");
        dup.Rows[0].Error.Should().BeNull();
        dup.Rows[1].Error.Should().Contain("duplicate");

        AttendanceCsvParser.Parse("student,classes\nx,1").HeaderError.Should().Contain("email, total, attended");
        AttendanceCsvParser.Parse("   \n").HeaderError.Should().Contain("empty");
    }

    // ── M9-U-05 attendance → eligibility ──────────────────────────────────────

    [Fact]
    public void M9_U_05_Decide_UnroundedBoundaryAndNotes()
    {
        EligibilityCalculator.Decide(true, 749, 1000, 75).Should().Be(new EligibilityDecision(false, 74.9, "Attendance 74.9% < 75%"));
        EligibilityCalculator.Decide(true, 750, 1000, 75).IsEligible.Should().BeTrue();
        EligibilityCalculator.Decide(true, 24, 40, 75).Note.Should().Be("Attendance 60% < 75%");
        EligibilityCalculator.Decide(true, 2, 3, 60).Note.Should().Be("Attendance 66.67% ≥ 60%");
    }

    [Fact]
    public void M9_U_05_Decide_NoRecordOrZeroClasses_IsNotComputed()
    {
        EligibilityCalculator.Decide(false, 0, 0, 75).Should().Match<EligibilityDecision>(d => !d.Computed && d.Note.Contains("No attendance record"));
        EligibilityCalculator.Decide(true, 0, 0, 75).Should().Match<EligibilityDecision>(d => !d.Computed && d.Percent == null);
        EligibilityCalculator.Percent(0, 0).Should().BeNull();
    }

    // ── M9-U-06 seat matching ─────────────────────────────────────────────────

    [Theory]
    // mode,        hostName,     seat,   wsIp,          actualMachine, actualIp,      expected
    [InlineData(SeatMatchMode.MachineName, null, "PC-01", "192.168.1.1", "pc-01 ", "10.0.0.9", true)]       // trimmed, case-insensitive
    [InlineData(SeatMatchMode.MachineName, null, "PC-01", "192.168.1.1", "PC-02", "192.168.1.1", false)]    // right IP doesn't help
    [InlineData(SeatMatchMode.MachineName, "LAB-PC-07", "R1C1", "", "LAB-PC-07", null, true)]              // HostName wins over the seat label
    [InlineData(SeatMatchMode.MachineName, "LAB-PC-07", "R1C1", "", "R1C1", null, false)]
    [InlineData(SeatMatchMode.MachineName, null, "PC-01", "", null, null, false)]                           // no identity → mismatch
    [InlineData(SeatMatchMode.Ip, null, "PC-01", "192.168.1.1", "RENAMED", "192.168.1.1", true)]
    [InlineData(SeatMatchMode.Ip, null, "PC-01", "192.168.1.1", "PC-01", "10.0.0.99", false)]
    [InlineData(SeatMatchMode.Ip, null, "PC-01", "", "PC-01", "10.0.0.99", false)]                          // no IP registered
    [InlineData(SeatMatchMode.Either, null, "PC-01", "192.168.1.1", "PC-01", "10.0.0.99", true)]
    [InlineData(SeatMatchMode.Either, null, "PC-01", "192.168.1.1", "X", "192.168.1.1", true)]
    [InlineData(SeatMatchMode.Either, null, "PC-01", "192.168.1.1", "X", "10.0.0.1", false)]
    public void M9_U_06_SeatMatches(SeatMatchMode mode, string? host, string seat, string wsIp, string? machine, string? ip, bool expected) =>
        SeatGuard.Matches(mode, host, seat, wsIp, machine, ip).Should().Be(expected);

    [Fact]
    public void M9_U_06_Ipv4MappedAddressesAreNormalised()
    {
        SeatGuard.NormalizeIp(IPAddress.Parse("::ffff:192.168.1.1")).Should().Be("192.168.1.1");
        SeatGuard.Matches(SeatMatchMode.Ip, null, "PC-01", "192.168.1.1", null, "::ffff:192.168.1.1").Should().BeTrue();
    }

    [Fact]
    public void M9_U_06_MismatchTexts_NameBothSeats()
    {
        var r = new SeatCheckResult(SeatStatus.Mismatch, "PC-01", null, "192.168.1.1", "PC-02", "10.0.0.5");
        r.Detail.Should().Be("logged in from PC-02, 10.0.0.5 (assigned PC-01)");
        r.StudentMessage.Should().Contain("seat PC-01").And.Contain("PC-02");
    }
}
