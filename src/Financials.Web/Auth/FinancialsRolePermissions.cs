// Moved to Financials.Application.Common.Authorization.FinancialsRolePermissions
// during the M-3 hardening pass so the role-permission map sits alongside
// the AuthorizationPolicies constants it references and so the contract test
// can validate it without taking a dependency on Web.
//
// This shim re-exports the moved type to keep any remaining internal Web
// references compiling. Remove on next sprint.

global using FinancialsRolePermissions = Financials.Application.Common.Authorization.FinancialsRolePermissions;
