﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web.Test.Common;
using NSubstitute;
using Xunit;

namespace Microsoft.Identity.Web.Test
{
    public class MicrosoftIdentityAuthenticationMessageHandlerTests
    {
        private const string HttpClientName = "test-client";

        private readonly AuthenticationResult _authenticationResult;
        private readonly MockHttpMessageHandler _mockedMessageHandler;
        private readonly MicrosoftIdentityAuthenticationMessageHandlerOptions _handlerOptions;
        private readonly MicrosoftIdentityOptions _identityOptions;

        public MicrosoftIdentityAuthenticationMessageHandlerTests()
        {
            _authenticationResult = GetAuthenticationResult();
            _mockedMessageHandler = new MockHttpMessageHandler();
            _handlerOptions = new MicrosoftIdentityAuthenticationMessageHandlerOptions
            {
                AuthenticationScheme = JwtBearerDefaults.AuthenticationScheme,
                IsProofOfPossessionRequest = false,
                Scopes = TestConstants.Scopes,
                Tenant = TestConstants.TenantIdAsGuid,
                TokenAcquisitionOptions = new TokenAcquisitionOptions(),
                UserFlow = TestConstants.B2CSignUpSignInUserFlow,
            };
            _identityOptions = new MicrosoftIdentityOptions();
        }

        private AuthenticationResult GetAuthenticationResult()
        {
            return new AuthenticationResult(
                "token",
                false,
                "id",
                DateTimeOffset.UtcNow.AddMinutes(1),
                DateTimeOffset.UtcNow.AddMinutes(2),
                TestConstants.TenantIdAsGuid,
                null,
                "id",
                Enumerable.Empty<string>(),
                Guid.NewGuid());
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task MicrosoftIdentityAuthenticationMessageHandler_Adds_AuthorizationHeader(bool useApp)
        {
            // arrange
            var tokenAcquisition = Substitute.For<ITokenAcquisition>();

            var options = Substitute.For<IOptionsMonitor<MicrosoftIdentityAuthenticationMessageHandlerOptions>>();
            options.CurrentValue.Returns(_handlerOptions);

            var services = new ServiceCollection();
            var builder = services.AddHttpClient(HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => _mockedMessageHandler);

            if (useApp)
            {
                tokenAcquisition.GetAuthenticationResultForAppAsync(default, default, default, default)
                    .ReturnsForAnyArgs(_authenticationResult);

                builder.AddHttpMessageHandler(() => new MicrosoftIdentityAppAuthenticationMessageHandler(tokenAcquisition, options));
            }
            else
            {
                tokenAcquisition.GetAuthenticationResultForUserAsync(default, default, default, default, default, default)
                    .ReturnsForAnyArgs(_authenticationResult);

                var identityOptions = Substitute.For<IOptionsMonitor<MicrosoftIdentityOptions>>();
                identityOptions.Get(string.Empty).Returns(_identityOptions);

                builder.AddHttpMessageHandler(() => new MicrosoftIdentityUserAuthenticationMessageHandler(tokenAcquisition, options, identityOptions));
            }

            var provider = services.BuildServiceProvider();
            var factory = provider.GetRequiredService<IHttpClientFactory>();

            var client = factory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Get, TestConstants.GraphBaseUrlBeta);

            // act
            var response = await client.SendAsync(request).ConfigureAwait(false);

            // assert
            if (useApp)
            {
                await tokenAcquisition.Received().GetAuthenticationResultForAppAsync(
                    _handlerOptions.Scopes,
                    _handlerOptions.AuthenticationScheme,
                    _handlerOptions.Tenant,
                    Arg.Any<TokenAcquisitionOptions>() /* options are cloned */)
                    .ConfigureAwait(false);
            }
            else
            {
                await tokenAcquisition.Received().GetAuthenticationResultForUserAsync(
                    Arg.Is<string[]>(scopes => scopes.SequenceEqual(_handlerOptions.GetScopes())),
                    authenticationScheme: _handlerOptions.AuthenticationScheme,
                    tenantId: _handlerOptions.Tenant,
                    userFlow: _handlerOptions.UserFlow,
                    tokenAcquisitionOptions: Arg.Any<TokenAcquisitionOptions>() /* options are cloned */)
                    .ConfigureAwait(false);
            }

            Assert.True(_mockedMessageHandler.Requests[0].Headers.Contains(Constants.Authorization));
            Assert.Equal($"Bearer {_authenticationResult.AccessToken}", _mockedMessageHandler.Requests[0].Headers.GetValues(Constants.Authorization).ElementAt(0));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task MicrosoftIdentityAuthenticationMessageHandler_Replaces_AuthorizationHeader(bool useApp)
        {
            // arrange
            var tokenAcquisition = Substitute.For<ITokenAcquisition>();

            var options = Substitute.For<IOptionsMonitor<MicrosoftIdentityAuthenticationMessageHandlerOptions>>();
            options.CurrentValue.Returns(_handlerOptions);

            var services = new ServiceCollection();
            var builder = services.AddHttpClient(HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => _mockedMessageHandler);

            if (useApp)
            {
                tokenAcquisition.GetAuthenticationResultForAppAsync(default, default, default, default)
                    .ReturnsForAnyArgs(_authenticationResult);

                builder.AddHttpMessageHandler(() => new MicrosoftIdentityAppAuthenticationMessageHandler(tokenAcquisition, options));
            }
            else
            {
                tokenAcquisition.GetAuthenticationResultForUserAsync(default, default, default, default, default, default)
                    .ReturnsForAnyArgs(_authenticationResult);

                var identityOptions = Substitute.For<IOptionsMonitor<MicrosoftIdentityOptions>>();
                identityOptions.Get(string.Empty).Returns(_identityOptions);

                builder.AddHttpMessageHandler(() => new MicrosoftIdentityUserAuthenticationMessageHandler(tokenAcquisition, options, identityOptions));
            }

            var provider = services.BuildServiceProvider();
            var factory = provider.GetRequiredService<IHttpClientFactory>();

            var client = factory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Get, TestConstants.GraphBaseUrlBeta);
            request.Headers.Add(Constants.Authorization, "auth");

            // act
            var response = await client.SendAsync(request).ConfigureAwait(false);

            // assert
            Assert.True(_mockedMessageHandler.Requests[0].Headers.Contains(Constants.Authorization));
            Assert.Equal($"Bearer {_authenticationResult.AccessToken}", _mockedMessageHandler.Requests[0].Headers.GetValues(Constants.Authorization).ElementAt(0));
        }

        private class MockHttpMessageHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode _statusCode;
            private readonly string _reason;
            private readonly HttpContent _content;
            private readonly List<HttpRequestMessage> _requests = new List<HttpRequestMessage>();

            public IReadOnlyList<HttpRequestMessage> Requests => _requests;

            public MockHttpMessageHandler(HttpStatusCode statusCode = HttpStatusCode.OK, HttpContent content = default, string reason = default)
            {
                _statusCode = statusCode;
                _reason = reason;
                _content = content;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                _requests.Add(request);

                return Task.FromResult(new HttpResponseMessage
                {
                    StatusCode = _statusCode,
                    ReasonPhrase = _reason,
                    Content = _content,
                });
            }
        }
    }
}
