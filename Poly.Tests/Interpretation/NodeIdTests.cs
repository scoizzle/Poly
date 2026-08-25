namespace Poly.Tests.Interpretation;

/// <summary>
/// Unit tests for NodeId value comparison semantics.
/// </summary>
public class NodeIdTests {
    [Test]
    public async Task NodeId_SameValue_AreEqual() {
        // Arrange
        var fromPosition = NodeId.FromPosition(1, 2);
        var fromParse = NodeId.Parse("node_1_2");

        // Act
        var equalsResult = fromPosition.Equals(fromParse);

        // Assert
        await Assert.That(equalsResult).IsTrue();
        await Assert.That(fromPosition).IsEqualTo(fromParse);
        await Assert.That(fromPosition.GetHashCode()).IsEqualTo(fromParse.GetHashCode());
    }

    [Test]
    public async Task NodeId_DifferentValues_AreNotEqual() {
        // Arrange
        var left = NodeId.FromPosition(1, 2);
        var right = NodeId.FromPosition(1, 3);

        // Act
        var equalsResult = left.Equals(right);

        // Assert
        await Assert.That(equalsResult).IsFalse();
        await Assert.That(left).IsNotEqualTo(right);
    }
}