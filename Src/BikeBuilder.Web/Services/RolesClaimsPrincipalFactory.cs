using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication.Internal;

namespace BikeBuilder.Web.Services;

// Auth0's post-login Action mints the roles claim as a JSON array ("https://bikebuilder/roles":
// ["Admin"]). Depending on how the default AccountClaimsPrincipalFactory maps it, that can
// surface as ONE claim whose value is the raw array text - which IsInRole never matches. This
// factory rewrites any array-shaped role claim into one claim per role; already-flat claims
// (the test stub emits plain strings) pass through untouched.
public class RolesClaimsPrincipalFactory(IAccessTokenProviderAccessor accessor)
    : AccountClaimsPrincipalFactory<RemoteUserAccount>(accessor)
{
  public override async ValueTask<ClaimsPrincipal> CreateUserAsync(
      RemoteUserAccount account, RemoteAuthenticationUserOptions options)
  {
    var user = await base.CreateUserAsync(account, options);
    if (user.Identity is not ClaimsIdentity { IsAuthenticated: true } identity)
      return user;

    foreach (var claim in identity.FindAll(options.RoleClaim).ToList())
    {
      if (!claim.Value.StartsWith('['))
        continue;

      identity.RemoveClaim(claim);
      foreach (var role in JsonSerializer.Deserialize<string[]>(claim.Value) ?? [])
        identity.AddClaim(new Claim(options.RoleClaim, role));
    }

    return user;
  }
}
