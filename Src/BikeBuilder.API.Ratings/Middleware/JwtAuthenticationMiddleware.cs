using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace BikeBuilder.API.Ratings.Middleware;

// Validates Auth0-issued access tokens the same way BikeBuilder.API's JwtBearer does:
// issuer taken from the authority's discovery document (so the stub issuer used in
// integration tests works), audience from config, sub as the name claim. Applied via
// UseWhen to the functions that require an authenticated user.
sealed class JwtAuthenticationMiddleware : IFunctionsWorkerMiddleware
{
  public const string UserContextKey = "User";

  // Function name -> roles allowed to invoke it. Functions not listed here only require a
  // valid token. CreateRating is BikeBuilder/Admin: rating lives on the bike-build surface.
  static readonly Dictionary<string, string[]> RequiredRoles = new()
  {
    ["CreateRating"] = [Roles.BikeBuilder, Roles.Admin],
  };

  readonly ConfigurationManager<OpenIdConnectConfiguration> _configurationManager;
  readonly JsonWebTokenHandler _tokenHandler = new();
  readonly string _audience;
  readonly string _roleClaim;

  public JwtAuthenticationMiddleware(IConfiguration config)
  {
    var authority = (config["Auth0:Authority"]
        ?? throw new InvalidOperationException("Auth0:Authority is not configured.")).TrimEnd('/');
    _audience = config["Auth0:Audience"]
        ?? throw new InvalidOperationException("Auth0:Audience is not configured.");
    // "role" in test mode (the stub issuer's plain claim), the Auth0 namespaced claim otherwise.
    _roleClaim = RoleClaim.Resolve(config[RoleClaim.ConfigKey]);
    _configurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
        $"{authority}/.well-known/openid-configuration",
        new OpenIdConnectConfigurationRetriever(),
        new HttpDocumentRetriever { RequireHttps = config.GetValue("Auth0:RequireHttpsMetadata", true) });
  }

  public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
  {
    var http = context.GetHttpContext()!;
    var header = http.Request.Headers.Authorization.ToString();
    if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
    {
      http.Response.StatusCode = StatusCodes.Status401Unauthorized;
      return;
    }

    var oidc = await _configurationManager.GetConfigurationAsync(context.CancellationToken);
    var result = await _tokenHandler.ValidateTokenAsync(header["Bearer ".Length..], new TokenValidationParameters
    {
      ValidIssuer = oidc.Issuer,
      ValidAudience = _audience,
      IssuerSigningKeys = oidc.SigningKeys,
      NameClaimType = "sub",
      // JsonWebTokenHandler does no inbound claim-type mapping, so the raw claim type from
      // the token is what IsInRole matches against.
      RoleClaimType = _roleClaim
    });

    if (!result.IsValid)
    {
      http.Response.StatusCode = StatusCodes.Status401Unauthorized;
      return;
    }

    var principal = new ClaimsPrincipal(result.ClaimsIdentity);
    if (RequiredRoles.TryGetValue(context.FunctionDefinition.Name, out var allowedRoles)
        && !allowedRoles.Any(principal.IsInRole))
    {
      http.Response.StatusCode = StatusCodes.Status403Forbidden;
      return;
    }

    context.Items[UserContextKey] = principal;
    await next(context);
  }
}
