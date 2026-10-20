using FluentAssertions;
using Student_Desktop_App.Core.Submission;

namespace Student_Desktop_App.Tests.Submission;

/// <summary>
/// M6-U-05 (TEST_CASES.md §M6): the desktop receipt hash equals the server's for the same answers.
/// The vectors are identical to Backend_API.Tests/Tests/Submission/ReceiptHasherTests.cs.
/// </summary>
public class ReceiptHasherTests
{
    private static readonly Guid Session = Guid.Parse("3f2504e0-4f89-41d3-9a0c-0305e82c3301");
    private static readonly Guid Q1 = Guid.Parse("0a6f3c2e-0000-4000-8000-000000000001");
    private static readonly Guid Q2 = Guid.Parse("b2c1f0a4-0000-4000-8000-000000000002");
    private static readonly Guid Q3 = Guid.Parse("c0ffee00-0000-4000-8000-000000000003");

    [Fact]
    public void M6_U_05_SameAnswers_SameSha256_AsTheServer()
    {
        ReceiptHasher.Compute(Session, new (Guid, string?)[]
            {
                (Q2, "A stack is LIFO; a queue is FIFO."),
                (Q1, "int main(){return 0;}"),
                (Q3, "")
            })
            .Should().Be("6c6bc5de890603b71932e3c4e7e57dc5fd9404fbab1a714ab10e079b14b3cd38");
        ReceiptHasher.Compute(Session, Array.Empty<(Guid, string?)>())
            .Should().Be("37fe9642560a5fc23f06e0a1b46024924c5e0015340830de100d6b9f4e8c3297");
        ReceiptHasher.Compute(Session, new (Guid, string?)[] { (Q1, "Ünïcødé ✓ x²") })
            .Should().Be("7f8ac1f45fa2c211a3d770eb49feb5ba3bb1c34ba70488466c78d885cbc63832");
    }

    [Fact]
    public void M6_U_05_AnyChangedCharacter_ChangesTheHash_AndMatchesIgnoresHexCase()
    {
        var original = ReceiptHasher.Compute(Session, new (Guid, string?)[] { (Q1, "x"), (Q2, "y") });
        ReceiptHasher.Compute(Session, new (Guid, string?)[] { (Q2, "y"), (Q1, "x") }).Should().Be(original);
        ReceiptHasher.Compute(Session, new (Guid, string?)[] { (Q1, "x"), (Q2, "Y") }).Should().NotBe(original);

        ReceiptHasher.Matches(original, original.ToUpperInvariant()).Should().BeTrue();
        ReceiptHasher.Matches(original, null).Should().BeFalse();
        ReceiptHasher.Matches("", "").Should().BeFalse();
    }
}
