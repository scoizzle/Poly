using Poly.Data.Modeling;
using Poly.Data.Modeling.Recipes;
using Poly.Data.Modeling.TypeSystem;
using Poly.Data.Modeling.Visual;
using Poly.Introspection;
using Poly.Syntax;

namespace Poly.Tests.Data.Modeling.Visual;

/// <summary>
/// Contract tests verifying the visual authoring sidecar:
/// 1. Visual layout immutability and transformations
/// 2. Stable node references (NodeId-based keying)
/// 3. Round-trip persistence (export/import)
/// 4. Visual metadata isolated from semantic domain state
/// 5. Projection endpoints for UI integration
/// </summary>
public class VisualAuthoringTests {

    // ── Visual Layout Immutability ────────────────────────────────────────────

    [Test]
    public async Task VisualLayout_IsImmutable() {
        var layout = VisualLayout.AtOrigin();

        var moved = layout.MoveTo(100, 200);

        // Original unchanged
        await Assert.That(layout.X).IsEqualTo(0);
        await Assert.That(layout.Y).IsEqualTo(0);

        // New instance has changes
        await Assert.That(moved.X).IsEqualTo(100);
        await Assert.That(moved.Y).IsEqualTo(200);
    }

    [Test]
    public async Task VisualLayout_ChainsTransformations() {
        var layout = VisualLayout.AtOrigin()
            .MoveTo(50, 100)
            .Resize(200, 150)
            .SetZOrder(5)
            .SetColor("#FF0000");

        await Assert.That(layout.X).IsEqualTo(50);
        await Assert.That(layout.Y).IsEqualTo(100);
        await Assert.That(layout.Width).IsEqualTo(200);
        await Assert.That(layout.Height).IsEqualTo(150);
        await Assert.That(layout.ZOrder).IsEqualTo(5);
        await Assert.That(layout.ColorHex).IsEqualTo("#FF0000");
    }

    [Test]
    public async Task VisualLayout_ToggleCollapsed() {
        var layout = VisualLayout.AtOrigin();

        await Assert.That(layout.IsCollapsed).IsFalse();

        var collapsed = layout.ToggleCollapsed();
        await Assert.That(collapsed.IsCollapsed).IsTrue();

        var expanded = collapsed.ToggleCollapsed();
        await Assert.That(expanded.IsCollapsed).IsFalse();
    }

    // ── Visual Metadata Store ─────────────────────────────────────────────────

    [Test]
    public async Task VisualMetadataStore_StoresAndRetrievesLayouts() {
        var store = new VisualMetadataStore();
        var nodeId = NodeId.NewId();
        var layout = VisualLayout.WithDimensions(10, 20, 100, 80);

        var updated = store.SetLayout(nodeId, layout);

        await Assert.That(updated.HasLayout(nodeId)).IsTrue();
        var retrieved = updated.GetLayout(nodeId);
        await Assert.That(retrieved.X).IsEqualTo(10);
        await Assert.That(retrieved.Y).IsEqualTo(20);
        await Assert.That(retrieved.Width).IsEqualTo(100);
        await Assert.That(retrieved.Height).IsEqualTo(80);
    }

    [Test]
    public async Task VisualMetadataStore_ReturnsDefaultLayoutForUnknownNode() {
        var store = new VisualMetadataStore();
        var unknownId = NodeId.NewId();

        var layout = store.GetLayout(unknownId);

        await Assert.That(layout.X).IsEqualTo(0);
        await Assert.That(layout.Y).IsEqualTo(0);
        await Assert.That(layout.Width).IsEqualTo(0);
        await Assert.That(layout.Height).IsEqualTo(0);
    }

    [Test]
    public async Task VisualMetadataStore_IsImmutable() {
        var store1 = new VisualMetadataStore();
        var nodeId = NodeId.NewId();
        var layout = VisualLayout.AtPosition(50, 50);

        var store2 = store1.SetLayout(nodeId, layout);

        // Original store unchanged
        await Assert.That(store1.HasLayout(nodeId)).IsFalse();
        await Assert.That(store1.Count).IsEqualTo(0);

        // New store has the layout
        await Assert.That(store2.HasLayout(nodeId)).IsTrue();
        await Assert.That(store2.Count).IsEqualTo(1);
    }

    [Test]
    public async Task VisualMetadataStore_UpdateLayout_AppliesTransformation() {
        var store = new VisualMetadataStore();
        var nodeId = NodeId.NewId();
        var initial = VisualLayout.AtOrigin();

        var updated = store
            .SetLayout(nodeId, initial)
            .UpdateLayout(nodeId, layout => layout.MoveTo(100, 200));

        var retrieved = updated.GetLayout(nodeId);
        await Assert.That(retrieved.X).IsEqualTo(100);
        await Assert.That(retrieved.Y).IsEqualTo(200);
    }

    [Test]
    public async Task VisualMetadataStore_ClearLayout_RemovesNode() {
        var store = new VisualMetadataStore();
        var nodeId = NodeId.NewId();
        var layout = VisualLayout.AtOrigin();

        var with = store.SetLayout(nodeId, layout);
        await Assert.That(with.HasLayout(nodeId)).IsTrue();

        var without = with.ClearLayout(nodeId);
        await Assert.That(without.HasLayout(nodeId)).IsFalse();
        // Cleared store should return default layout
        var defaultLayout = without.GetLayout(nodeId);
        await Assert.That(defaultLayout.X).IsEqualTo(0);
    }

    [Test]
    public async Task VisualMetadataStore_BatchUpdate_IsAtomic() {
        var store = new VisualMetadataStore();
        var id1 = NodeId.NewId();
        var id2 = NodeId.NewId();
        var id3 = NodeId.NewId();

        var updates = new Dictionary<NodeId, VisualLayout> {
            { id1, VisualLayout.AtPosition(10, 20) },
            { id2, VisualLayout.AtPosition(30, 40) },
            { id3, VisualLayout.AtPosition(50, 60) }
        };

        var updated = store.BatchUpdate(updates);

        await Assert.That(updated.Count).IsEqualTo(3);
        await Assert.That(updated.GetLayout(id1).X).IsEqualTo(10);
        await Assert.That(updated.GetLayout(id2).X).IsEqualTo(30);
        await Assert.That(updated.GetLayout(id3).X).IsEqualTo(50);
    }

    // ── Round-Trip Persistence ────────────────────────────────────────────────

    [Test]
    public async Task VisualMetadata_ExportImportRoundTrip_PreservesState() {
        var store1 = new VisualMetadataStore();
        var id1 = NodeId.NewId();
        var id2 = NodeId.NewId();

        var layout1 = VisualLayout.WithDimensions(10, 20, 100, 80).SetZOrder(3).SetColor("#FF0000");
        var layout2 = VisualLayout.WithDimensions(200, 300, 400, 500).ToggleCollapsed();

        var populated = store1
            .SetLayout(id1, layout1)
            .SetLayout(id2, layout2);

        // Export
        var exported = populated.ExportMetadata();

        // Import
        var store2 = VisualMetadataStore.ImportMetadata(exported);

        // Verify state preserved
        var restored1 = store2.GetLayout(id1);
        await Assert.That(restored1.X).IsEqualTo(10);
        await Assert.That(restored1.Y).IsEqualTo(20);
        await Assert.That(restored1.Width).IsEqualTo(100);
        await Assert.That(restored1.Height).IsEqualTo(80);
        await Assert.That(restored1.ZOrder).IsEqualTo(3);
        await Assert.That(restored1.ColorHex).IsEqualTo("#FF0000");

        var restored2 = store2.GetLayout(id2);
        await Assert.That(restored2.X).IsEqualTo(200);
        await Assert.That(restored2.IsCollapsed).IsTrue();
    }

    [Test]
    public async Task VisualMetadata_RoundTrip_WithEmptyStore() {
        var store1 = new VisualMetadataStore();

        var exported = store1.ExportMetadata();
        var store2 = VisualMetadataStore.ImportMetadata(exported);

        await Assert.That(store2.Count).IsEqualTo(0);
    }

    // ── Projection Endpoints ──────────────────────────────────────────────────

    [Test]
    public async Task VisualProjectionEndpoint_ProjectsDomain() {
        var domain = new Domain("TestDomain");
        var stringType = new Primitive(domain, "String", TypeCategory.Text);
        domain.AddType(stringType);

        var endpoint = new VisualProjectionEndpoint(domain);
        var projection = endpoint.ProjectDomain();

        await Assert.That(projection.NodeId).IsEqualTo(domain.Id);
        await Assert.That(projection.EntityName).IsEqualTo("TestDomain");
        await Assert.That(projection.EntityType).IsEqualTo("Domain");
        await Assert.That(projection.ChildProjections.Count).IsEqualTo(1);
    }

    [Test]
    public async Task VisualProjectionEndpoint_ProjectsEntity() {
        var domain = new Domain("TestDomain");
        var entity = new Entity(domain, "Order");
        domain.AddType(entity);

        var mutation = domain.CreateMutation();
        var stringType = new Primitive(domain, "String", TypeCategory.Text);
        mutation.AddType(stringType);
        mutation.AddProperty(entity, new Property(domain, "id", stringType));
        mutation.AddProperty(entity, new Property(domain, "name", stringType));
        mutation.AddStage(entity, new Stage(domain, "Draft"));
        mutation.Apply();

        var endpoint = new VisualProjectionEndpoint(domain);
        var projection = endpoint.ProjectType(entity);

        await Assert.That(projection.EntityName).IsEqualTo("Order");
        await Assert.That(projection.EntityType).IsEqualTo("Entity");
        // Should contain properties and stages
        await Assert.That(projection.ChildProjections.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task VisualProjectionEndpoint_UpdateNodeLayout() {
        var domain = new Domain("TestDomain");
        var entity = new Entity(domain, "Order");
        domain.AddType(entity);

        var endpoint = new VisualProjectionEndpoint(domain);
        var layout = VisualLayout.AtPosition(100, 200);

        var updated = endpoint.UpdateNodeLayout(entity.Id, layout);

        await Assert.That(updated.HasLayout(entity.Id)).IsTrue();
        var retrieved = updated.GetLayout(entity.Id);
        await Assert.That(retrieved.X).IsEqualTo(100);
        await Assert.That(retrieved.Y).IsEqualTo(200);
    }

    [Test]
    public async Task VisualProjectionEndpoint_RestoreVisualMetadata() {
        var domain = new Domain("TestDomain");
        var entity = new Entity(domain, "Order");
        domain.AddType(entity);

        // Create visual metadata externally
        var store1 = new VisualMetadataStore();
        var stored = store1.SetLayout(entity.Id, VisualLayout.AtPosition(50, 75));

        // Create endpoint and restore
        var endpoint = new VisualProjectionEndpoint(domain);
        endpoint.RestoreVisualMetadata(stored);

        var layout = endpoint.VisualMetadata.GetLayout(entity.Id);
        await Assert.That(layout.X).IsEqualTo(50);
        await Assert.That(layout.Y).IsEqualTo(75);
    }

    // ── Round-Trip Integrity ──────────────────────────────────────────────────

    [Test]
    public async Task VisualAuthoringDoesNotCorruptDomainState() {
        var domain = new Domain("TestDomain");
        var entity = new Entity(domain, "Order");
        domain.AddType(entity);

        var endpoint = new VisualProjectionEndpoint(domain);

        // Perform many visual mutations
        for (int i = 0; i < 10; i++) {
            endpoint.MoveNode(entity.Id, i * 10, i * 20);
            endpoint.ResizeNode(entity.Id, 100 + i, 80 + i);
        }

        // Domain state should be unchanged
        await Assert.That(domain.Types.Count).IsEqualTo(1);
        await Assert.That(domain.Types.First().Name).IsEqualTo("Order");

        // Visual metadata should have final state
        var layout = endpoint.VisualMetadata.GetLayout(entity.Id);
        await Assert.That(layout.X).IsEqualTo(90);  // Last move: 9 * 10
        await Assert.That(layout.Y).IsEqualTo(180); // Last move: 9 * 20
        await Assert.That(layout.Width).IsEqualTo(109); // Last resize: 100 + 9
        await Assert.That(layout.Height).IsEqualTo(89); // Last resize: 80 + 9
    }

    [Test]
    public async Task VisualProjection_HasCustomLayout_ChecksDefaults() {
        var domain = new Domain("TestDomain");
        var entity = new Entity(domain, "Order");
        domain.AddType(entity);

        var endpoint = new VisualProjectionEndpoint(domain);
        var projection1 = endpoint.ProjectDomain();

        // Before any visual changes
        await Assert.That(projection1.HasCustomLayout).IsFalse();

        // After visual change
        endpoint.MoveNode(entity.Id, 50, 50);
        var projection2 = endpoint.ProjectDomain();

        await Assert.That(projection2.HasCustomLayout).IsFalse(); // Domain unchanged
    }

    [Test]
    public async Task VisualProjection_ExportImportPreservesIntegrity() {
        var domain = new Domain("TestDomain");
        var entity = new Entity(domain, "Order");
        domain.AddType(entity);

        var endpoint1 = new VisualProjectionEndpoint(domain);
        endpoint1.MoveNode(entity.Id, 42, 84);
        endpoint1.SetNodeColor(entity.Id, "#123456");

        // Export
        var exported = endpoint1.ExportVisualMetadata();

        // Create new endpoint and import
        var endpoint2 = new VisualProjectionEndpoint(domain);
        endpoint2.ImportVisualMetadata(exported);

        // Verify
        var layout = endpoint2.VisualMetadata.GetLayout(entity.Id);
        await Assert.That(layout.X).IsEqualTo(42);
        await Assert.That(layout.Y).IsEqualTo(84);
        await Assert.That(layout.ColorHex).IsEqualTo("#123456");
    }

    // ── Complex Scenario Tests ────────────────────────────────────────────────

    [Test]
    public async Task VisualMetadataStore_MultipleUpdatesYieldConsistentState() {
        var store = new VisualMetadataStore();
        var nodeId = NodeId.NewId();

        var step1 = store.SetLayout(nodeId, VisualLayout.AtOrigin());
        var step2 = step1.UpdateLayout(nodeId, l => l.MoveTo(10, 20));
        var step3 = step2.UpdateLayout(nodeId, l => l.Resize(100, 50));
        var step4 = step3.UpdateLayout(nodeId, l => l.SetZOrder(3));
        var step5 = step4.UpdateLayout(nodeId, l => l.SetColor("#FF0000"));

        var final = step5.GetLayout(nodeId);

        await Assert.That(final.X).IsEqualTo(10);
        await Assert.That(final.Y).IsEqualTo(20);
        await Assert.That(final.Width).IsEqualTo(100);
        await Assert.That(final.Height).IsEqualTo(50);
        await Assert.That(final.ZOrder).IsEqualTo(3);
        await Assert.That(final.ColorHex).IsEqualTo("#FF0000");
    }

    [Test]
    public async Task VisualProjectionEndpoint_MultipleEntityTypes() {
        var domain = new Domain("TestDomain");
        var entity1 = new Entity(domain, "Order");
        var entity2 = new Entity(domain, "Customer");
        domain.AddType(entity1);
        domain.AddType(entity2);

        var endpoint = new VisualProjectionEndpoint(domain);
        endpoint.MoveNode(entity1.Id, 10, 20);
        endpoint.MoveNode(entity2.Id, 100, 200);

        var layout1 = endpoint.VisualMetadata.GetLayout(entity1.Id);
        var layout2 = endpoint.VisualMetadata.GetLayout(entity2.Id);

        await Assert.That(layout1.X).IsEqualTo(10);
        await Assert.That(layout1.Y).IsEqualTo(20);
        await Assert.That(layout2.X).IsEqualTo(100);
        await Assert.That(layout2.Y).IsEqualTo(200);
    }

    [Test]
    public async Task VisualMetadataStore_LargeScale_BatchInsertRetrieve() {
        var store = new VisualMetadataStore();
        var updates = new Dictionary<NodeId, VisualLayout>();

        // Create 100 layouts
        for (int i = 0; i < 100; i++) {
            updates[NodeId.NewId()] = VisualLayout.AtPosition(i * 10, i * 20);
        }

        var populated = store.BatchUpdate(updates);

        await Assert.That(populated.Count).IsEqualTo(100);

        // Verify a few specific entries
        var keys = updates.Keys.ToList();
        var layout0 = populated.GetLayout(keys[0]);
        var layout50 = populated.GetLayout(keys[50]);
        var layout99 = populated.GetLayout(keys[99]);

        await Assert.That(layout0.X).IsEqualTo(0);
        await Assert.That(layout50.X).IsEqualTo(500);
        await Assert.That(layout99.X).IsEqualTo(990);
    }

    [Test]
    public async Task VisualLayout_Rotation_SetsCorrectValue() {
        var layout = VisualLayout.AtOrigin()
            .Rotate(45);

        await Assert.That(layout.RotationDegrees).IsEqualTo(45);

        var rotated90 = layout.Rotate(90);
        await Assert.That(rotated90.RotationDegrees).IsEqualTo(90);
    }

    [Test]
    public async Task VisualProjectionEndpoint_ClearAllLayouts() {
        var domain = new Domain("TestDomain");
        var entity1 = new Entity(domain, "Order");
        var entity2 = new Entity(domain, "Customer");
        domain.AddType(entity1);
        domain.AddType(entity2);

        var endpoint = new VisualProjectionEndpoint(domain);
        endpoint.MoveNode(entity1.Id, 10, 20);
        endpoint.MoveNode(entity2.Id, 100, 200);

        await Assert.That(endpoint.VisualMetadata.Count).IsEqualTo(2);

        endpoint.ClearAllLayouts();

        await Assert.That(endpoint.VisualMetadata.Count).IsEqualTo(0);
    }

    [Test]
    public async Task VisualProjectionEndpoint_ClearSpecificNodeLayout() {
        var domain = new Domain("TestDomain");
        var entity1 = new Entity(domain, "Order");
        var entity2 = new Entity(domain, "Customer");
        domain.AddType(entity1);
        domain.AddType(entity2);

        var endpoint = new VisualProjectionEndpoint(domain);
        endpoint.MoveNode(entity1.Id, 10, 20);
        endpoint.MoveNode(entity2.Id, 100, 200);

        await Assert.That(endpoint.VisualMetadata.Count).IsEqualTo(2);

        endpoint.ClearNodeLayout(entity1.Id);

        await Assert.That(endpoint.VisualMetadata.Count).IsEqualTo(1);
        await Assert.That(endpoint.VisualMetadata.HasLayout(entity2.Id)).IsTrue();
        await Assert.That(endpoint.VisualMetadata.HasLayout(entity1.Id)).IsFalse();
    }

    [Test]
    public async Task VisualLayout_DefaultValues_AreReasonable() {
        var layout = VisualLayout.AtOrigin();

        await Assert.That(layout.X).IsEqualTo(0);
        await Assert.That(layout.Y).IsEqualTo(0);
        await Assert.That(layout.Width).IsEqualTo(0);
        await Assert.That(layout.Height).IsEqualTo(0);
        await Assert.That(layout.RotationDegrees).IsEqualTo(0);
        await Assert.That(layout.ZOrder).IsEqualTo(0);
        await Assert.That(layout.IsCollapsed).IsFalse();
        await Assert.That(layout.ColorHex).IsNull();
    }

    [Test]
    public async Task VisualMetadata_ExportFormat_IsDeserializable() {
        var store = new VisualMetadataStore();
        var nodeId = NodeId.NewId();
        var layout = VisualLayout.WithDimensions(10, 20, 100, 80)
            .SetZOrder(5)
            .SetColor("#AABBCC")
            .ToggleCollapsed();

        var populated = store.SetLayout(nodeId, layout);
        var exported = populated.ExportMetadata();

        // Check format - should be Dictionary<string, (double, double, double, double, int, bool, string?)>
        await Assert.That(exported).IsNotNull();
        await Assert.That(exported.Count).IsEqualTo(1);

        var key = nodeId.Value;
        await Assert.That(exported.ContainsKey(key)).IsTrue();

        var (x, y, w, h, z, collapsed, color) = exported[key];
        await Assert.That(x).IsEqualTo(10);
        await Assert.That(y).IsEqualTo(20);
        await Assert.That(w).IsEqualTo(100);
        await Assert.That(h).IsEqualTo(80);
        await Assert.That(z).IsEqualTo(5);
        await Assert.That(collapsed).IsTrue();
        await Assert.That(color).IsEqualTo("#AABBCC");
    }

    [Test]
    public async Task VisualProjection_ExportsEmptyMetadataGracefully() {
        var domain = new Domain("TestDomain");
        var entity = new Entity(domain, "Order");
        domain.AddType(entity);

        var endpoint = new VisualProjectionEndpoint(domain);
        // Don't set any layouts

        var exported = endpoint.ExportVisualMetadata();

        await Assert.That(exported.Count).IsEqualTo(0);
    }
}