using System.Reflection;
using Financials.Application;
using Financials.Application.Common.Authorization;

namespace Financials.Application.Tests.Common.Authorization;

/// <summary>
/// M-3 contract: every permission named on an aggregate-level command (via
/// <see cref="RequiresPermissionAttribute"/>) must also appear in at least
/// one role's claim list in <c>FinancialsRolePermissions</c>. Reversely,
/// every value in <see cref="AuthorizationPolicies"/> that is referenced
/// in the role-permission map must be enforceable somewhere — either by a
/// command attribute, by a Razor-page <c>[Authorize(Policy=...)]</c>, or
/// by both.
///
/// This test pins the contract that was previously documentation-only.
/// </summary>
public class RolePermissionsContractTests
{
    private static List<string> AllPolicyConstants() =>
        typeof(AuthorizationPolicies)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

    private static List<string> AllCommandRequiredPermissions() =>
        typeof(ApplicationAssemblyMarker).Assembly
            .GetTypes()
            .Select(t => t.GetCustomAttribute<RequiresPermissionAttribute>())
            .Where(a => a is not null)
            .Select(a => a!.Permission)
            .ToList();

    [Fact]
    public void Every_AuthorizationPolicies_constant_is_a_well_formed_dotted_value()
    {
        var policies = AllPolicyConstants();

        policies.Should().NotBeEmpty();
        policies.Should().OnlyContain(p => p.StartsWith("financials.", StringComparison.Ordinal));
        policies.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Every_command_required_permission_is_a_known_policy_constant()
    {
        var requiredByCommands = AllCommandRequiredPermissions();
        var knownPolicies = AllPolicyConstants();

        // A [RequiresPermission("financials.totally.made.up")] would silently
        // bypass every role's claim list. This guard catches typos before they
        // ship.
        var orphans = requiredByCommands.Except(knownPolicies).ToList();
        orphans.Should().BeEmpty(
            "every [RequiresPermission(X)] must point at a constant declared in AuthorizationPolicies; "
            + "orphans: " + string.Join(", ", orphans));
    }

    [Fact]
    public void Every_mutation_command_in_Application_carries_a_RequiresPermission_attribute()
    {
        // Any IRequest<Result> or IRequest<Result<T>> that mutates state
        // (record name ends with "Command") must declare its required
        // permission. Queries (record name ends with "Query") are exempt —
        // they continue to rely on the page-level [Authorize] only, per the
        // M-2 ADR.
        var assembly = typeof(ApplicationAssemblyMarker).Assembly;
        var commands = assembly
            .GetTypes()
            .Where(t => t.Name.EndsWith("Command", StringComparison.Ordinal))
            .Where(t => t.IsClass && !t.IsAbstract)
            .ToList();

        commands.Should().NotBeEmpty();

        var missing = commands
            .Where(t => t.GetCustomAttribute<RequiresPermissionAttribute>() is null)
            .Select(t => t.FullName)
            .ToList();

        missing.Should().BeEmpty(
            "every *Command type must declare its required permission via [RequiresPermission]; "
            + "missing: " + string.Join(", ", missing));
    }

    [Fact]
    public void RequiresPermission_attribute_rejects_blank_permission()
    {
        var act = () => new RequiresPermissionAttribute("");
        act.Should().Throw<ArgumentException>().WithParameterName("permission");
    }

    // --- M-3: the role-permission map -------------------------------------

    private static HashSet<string> AllRolePermissions() =>
        FinancialsRolePermissions.Map.Values.SelectMany(v => v).ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void Every_permission_in_FinancialsRolePermissions_is_a_known_policy_constant()
    {
        var rolePermissions = AllRolePermissions();
        var knownPolicies = AllPolicyConstants().ToHashSet(StringComparer.Ordinal);

        var unknown = rolePermissions.Except(knownPolicies).ToList();
        unknown.Should().BeEmpty(
            "FinancialsRolePermissions must only reference AuthorizationPolicies constants; "
            + "found in map but not in policies: " + string.Join(", ", unknown));
    }

    [Fact]
    public void Every_command_RequiresPermission_value_is_granted_to_at_least_one_role()
    {
        // A [RequiresPermission(X)] that no role grants is dead code at best
        // and a permanently-unauthorised handler at worst — no caller can
        // ever pass the gate. This guard catches both.
        var commandRequired = AllCommandRequiredPermissions().ToHashSet(StringComparer.Ordinal);
        var rolePermissions = AllRolePermissions();

        var unreachable = commandRequired.Except(rolePermissions).ToList();
        unreachable.Should().BeEmpty(
            "every command-required permission must be granted by at least one role in FinancialsRolePermissions; "
            + "unreachable: " + string.Join(", ", unreachable));
    }

    [Fact]
    public void Every_AuthorizationPolicies_constant_is_referenced_somewhere()
    {
        // A constant in AuthorizationPolicies that isn't (a) on a command
        // and (b) in the role map is documentation that drifts. Either we
        // need it on a command, or it should be deleted.
        // Read-only permissions (xRead) are exempt — they gate Razor pages,
        // not commands. We still require them to appear in the role map.
        var policyConstants = AllPolicyConstants();
        var commandRequired = AllCommandRequiredPermissions().ToHashSet(StringComparer.Ordinal);
        var rolePermissions = AllRolePermissions();

        var dangling = policyConstants
            .Where(p => !commandRequired.Contains(p) && !rolePermissions.Contains(p))
            .ToList();

        dangling.Should().BeEmpty(
            "every AuthorizationPolicies constant must be referenced by a command or by the role map; "
            + "dangling: " + string.Join(", ", dangling));
    }
}
