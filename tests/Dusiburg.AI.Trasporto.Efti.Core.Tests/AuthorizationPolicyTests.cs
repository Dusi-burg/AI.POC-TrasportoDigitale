using Dusiburg.AI.Trasporto.Efti.Core.Security;
using Dusiburg.AI.Trasporto.Shared.Security;

namespace Dusiburg.AI.Trasporto.Efti.Core.Tests;

/// <summary>
/// La matrice dei ruoli, cella per cella. Il test ripete la matrice a mano: cambiare un permesso richiede di cambiarlo in due
/// posti, e questa è la sua funzione.
/// </summary>
public class AuthorizationPolicyTests
{
    [TestCase(Operation.IdentityRead, AccessScope.Self, AccessScope.Self, AccessScope.Self, AccessScope.Self)]
    [TestCase(Operation.ShipmentList, AccessScope.AllShipments, AccessScope.AllShipments, AccessScope.Denied, AccessScope.AllShipments)]
    [TestCase(Operation.ShipmentRead, AccessScope.AllShipments, AccessScope.AllShipments, AccessScope.Denied, AccessScope.AllShipments)]
    [TestCase(Operation.ShipmentImport, AccessScope.AllShipments, AccessScope.Denied, AccessScope.Denied, AccessScope.Denied)]
    [TestCase(Operation.DatasetExport, AccessScope.AllShipments, AccessScope.AllShipments, AccessScope.Denied, AccessScope.AllShipments)]
    [TestCase(Operation.IdentifierSearch, AccessScope.Denied, AccessScope.Denied, AccessScope.IdentifierRegistry, AccessScope.Denied)]
    [TestCase(Operation.SubsetRead, AccessScope.Denied, AccessScope.Denied, AccessScope.AuthoritySubset, AccessScope.Denied)]
    [TestCase(Operation.AuthorityAccessRead, AccessScope.AuthorityAccesses, AccessScope.Denied, AccessScope.Denied, AccessScope.AuthorityAccesses)]
    [TestCase(Operation.AuditRead, AccessScope.Denied, AccessScope.Denied, AccessScope.Denied, AccessScope.AuditTrail)]
    public void ScopeFor_Operation_MatchesTheMatrix(
        Operation operation, AccessScope dispatcher, AccessScope claimsManager, AccessScope authority, AccessScope admin)
    {
        //SUT
        AccessScope[] scopes = [.. Enum.GetValues<Role>().Select(role => AuthorizationPolicy.ScopeFor(role, operation))];

        Assert.That(scopes, Is.EqualTo(new[] { dispatcher, claimsManager, authority, admin }));
        Assert.That(Enum.GetValues<Role>().Select(role => AuthorizationPolicy.IsAllowed(role, operation)), Is.EqualTo(scopes.Select(s => s != AccessScope.Denied)));
    }

    [Test]
    public void Matrix_CoversEveryOperation_WithoutDuplicateCells()
    {
        //SUT
        IReadOnlyList<Permission> permissions = AuthorizationPolicy.Permissions;

        Assert.That(AuthorizationPolicy.Operations.Select(o => o.Operation), Is.EquivalentTo(Enum.GetValues<Operation>()));
        Assert.That(permissions.Select(p => (p.Role, p.Operation)), Is.Unique);
        Assert.That(permissions.Select(p => p.Scope), Has.None.EqualTo(AccessScope.Denied), "si elenca solo ciò che è consentito");
        Assert.That(permissions.Select(p => p.Operation).Distinct(), Is.EquivalentTo(Enum.GetValues<Operation>()), "nessuna operazione irraggiungibile");
    }

    [Test]
    public void Authority_ReachesShipmentsOnlyThroughTheGate()
    {
        //SUT
        IReadOnlyList<Operation> allowed = AuthorizationPolicy.AllowedOperations(Role.Authority);

        Assert.That(allowed, Is.EqualTo(new[] { Operation.IdentityRead, Operation.IdentifierSearch, Operation.SubsetRead }));
    }

    [Test]
    public void OnlyTheDispatcher_CanWrite()
    {
        //SUT
        var writers = AuthorizationPolicy.Operations
            .Where(o => o.IsWrite)
            .SelectMany(o => Enum.GetValues<Role>().Where(role => AuthorizationPolicy.IsAllowed(role, o.Operation)))
            .Distinct();

        Assert.That(writers, Is.EqualTo(new[] { Role.Dispatcher }));
    }

    [Test]
    public void DemoActors_OnePerRole()
    {
        //SUT
        IReadOnlyList<Actor> actors = DemoActors.All;

        Assert.That(actors.Select(a => a.Role), Is.EquivalentTo(Enum.GetValues<Role>()));
        Assert.That(actors.Select(a => a.Id), Is.Unique);
    }
}
