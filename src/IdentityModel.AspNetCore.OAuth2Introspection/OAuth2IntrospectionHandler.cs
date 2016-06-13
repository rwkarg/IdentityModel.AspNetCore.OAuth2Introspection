﻿// Copyright (c) Dominick Baier & Brock Allen. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.


using IdentityModel.Client;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Authentication;
using Microsoft.Extensions.Caching.Distributed;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace IdentityModel.AspNetCore.OAuth2Introspection
{
    public class OAuth2IntrospectionHandler : AuthenticationHandler<OAuth2IntrospectionOptions>
    {
        private readonly IDistributedCache _cache;
        private readonly IntrospectionClient _client;

        public OAuth2IntrospectionHandler(IntrospectionClient client, IDistributedCache cache)
        {
            _client = client;
            _cache = cache;
        }

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            string token = Options.TokenRetriever(Context.Request);

            if (token.IsMissing())
            {
                return AuthenticateResult.Skip();
            }

            if (token.Contains('.') && Options.SkipTokensWithDots)
            {
                return AuthenticateResult.Skip();
            }

            if (Options.EnableCaching)
            {
                var claims = await _cache.GetClaimsAsync(token);
                if (claims != null)
                {
                    return AuthenticateResult.Success(CreateTicket(claims));
                }
            }

            var response = await _client.SendAsync(new IntrospectionRequest
            {
                Token = token,
                ClientId = Options.ScopeName,
                ClientSecret = Options.ScopeSecret
            });

            if (response.IsError)
            {
                return AuthenticateResult.Fail("Error returned from introspection endpoint: " + response.Error);
            }

            if (response.IsActive)
            {
                var ticket = CreateTicket(response.Claims.ToClaims());

                if (Options.SaveToken)
                {
                    ticket.Properties.StoreTokens(new[]
                        {
                            new AuthenticationToken { Name = "access_token", Value = token }
                        });
                }

                if (Options.EnableCaching)
                {
                    await _cache.SetTuplesAsync(token, response.Claims, Options.CacheDuration);
                }

                return AuthenticateResult.Success(ticket);
            }
            else
            {
                return AuthenticateResult.Fail("Token is not active.");
            }
        }

        private AuthenticationTicket CreateTicket(List<Claim> claims)
        {
            var id = new ClaimsIdentity(claims, Options.AuthenticationScheme, Options.NameClaimType, Options.RoleClaimType);
            var principal = new ClaimsPrincipal(id);

            return new AuthenticationTicket(principal, new AuthenticationProperties(), Options.AuthenticationScheme);
        }
    }
}