using Backend_API.Services;
using FluentAssertions;

namespace Backend_API.Tests.Tests.Submission;

/// <summary>
/// M6-U-05 (server half). The desktop app's <c>Core/Submission/ReceiptHasher</c> must produce the same
/// hashes for the same input — Student_Desktop_App.Tests asserts the identical vectors.
/// </summary>
public class ReceiptHasherTests
{
    private static readonly Guid Session = Guid.Parse("3f2504e0-4f89-41d3-9a0c-0305e82c3301");
    private static readonly Guid Q1 = Guid.Parse("0a6f3c2e-0000-4000-8000-000000000001");
    private static readonly Guid Q2 = Guid.Parse("b2c1f0a4-0000-4000-8000-000000000002");
    private static readonly Guid Q3 = Guid.Parse("c0ffee00-0000-4000-8000-000000000003");

    [Fact]
    public void M6_U_05_FixedVector_ThreeAnswers()
    {
        ReceiptHasher.Compute(Session, new (Guid, string?)[]
            {
                (Q2, "A stack is LIFO; a queue is FIFO."),
                (Q1, "int main(){return 0;}"),
                (Q3, "")
            })
            .Should().Be("6c6bc5de890603b71932e3c4e7e57dc5fd9404fbab1a714ab10e079b14b3cd38");
    }

    [Fact]
    public void M6_U_05_FixedVector_NoAnswers_AndUnicode()
    {
        ReceiptHasher.Compute(Session, Array.Empty<(Guid, string?)>())
            .Should().Be("37fe9642560a5fc23f06e0a1b46024924c5e0015340830de100d6b9f4e8c3297");
        ReceiptHasher.Compute(Session, new (Guid, string?)[] { (Q1, "Ünïcødé ✓ x²") })
            .Should().Be("7f8ac1f45fa2c211a3d770eb49feb5ba3bb1c34ba70488466c78d885cbc63832");
    }

    [Fact]
    public void OrderOfInput_DoesNotMatter_ButEveryCharacterDoes()
    {
        var a = ReceiptHasher.Compute(Session, new (Guid, string?)[] { (Q1, "x"), (Q2, "y") });
        var b = ReceiptHasher.Compute(Session, new (Guid, string?)[] { (Q2, "y"), (Q1, "x") });
        a.Should().Be(b);

        ReceiptHasher.Compute(Session, new (Guid, string?)[] { (Q1, "x "), (Q2, "y") }).Should().NotBe(a);
        ReceiptHasher.Compute(Guid.NewGuid(), new (Guid, string?)[] { (Q1, "x"), (Q2, "y") }).Should().NotBe(a);
        // Separators keep "ab"+"c" and "a"+"bc" apart.
        ReceiptHasher.Compute(Session, new (Guid, string?)[] { (Q1, "ab"), (Q2, "c") })
            .Should().NotBe(ReceiptHasher.Compute(Session, new (Guid, string?)[] { (Q1, "a"), (Q2, "bc") }));
        // A null answer text hashes like an empty one (the server stores "").
        ReceiptHasher.Compute(Session, new (Guid, string?)[] { (Q1, null) })
            .Should().Be(ReceiptHasher.Compute(Session, new (Guid, string?)[] { (Q1, "") }));
    }
}
