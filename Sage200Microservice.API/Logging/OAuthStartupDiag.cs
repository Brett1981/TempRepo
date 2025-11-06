namespace Sage200Microservice.API.Logging
{
    public static class OAuthStartupDiag
    {
        /// <summary>Call once in Program.cs after building the app.</summary>
        public static void LogOAuthConfig(IConfiguration cfg, ILogger logger)
        {
            logger.LogInformation(
                "OAuth cfg => RedirectUri={Redirect} | AuthZ={AuthZ} | Token={Token} | Scopes={Scopes} | Audience={Audience}",
                cfg["SageApi:RedirectUri"],
                cfg["SageApi:AuthorizationEndpoint"],
                cfg["SageApi:TokenEndpoint"],
                cfg["SageApi:Scopes"],
                cfg["SageApi:Audience"]);
        }
    }
}
