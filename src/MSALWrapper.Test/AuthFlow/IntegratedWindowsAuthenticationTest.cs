// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Authentication.MSALWrapper.Test
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    using FluentAssertions;

    using Microsoft.Authentication.MSALWrapper;
    using Microsoft.Authentication.MSALWrapper.AuthFlow;
    using Microsoft.Authentication.TestHelper;
    using Microsoft.Extensions.Logging;
    using Microsoft.Identity.Client;
    using Microsoft.IdentityModel.JsonWebTokens;

    using Moq;

    using NUnit.Framework;

    public class IntegratedWindowsAuthenticationTest
    {
        private const string MsalServiceExceptionErrorCode = "1";
        private const string MsalServiceExceptionMessage = "MSAL Service Exception: Something bad has happened!";
        private const string TestUser = "user@microsoft.com";

        // These Guids were randomly generated and do not refer to a real resource or client
        // as we don't need either for our testing.
        private static readonly Guid ResourceId = new Guid("6e979987-a7c8-4604-9b37-e51f06f08f1a");
        private static readonly Guid ClientId = new Guid("5af6def2-05ec-4cab-b9aa-323d75b5df40");
        private static readonly Guid TenantId = new Guid("8254f6f7-a09f-4752-8bd6-391adc3b912e");

        private ILogger logger;

        // MSAL Specific Mocks
        private Mock<IPCAWrapper> pcaWrapperMock;
        private Mock<IAccount> testAccount;
        private IEnumerable<string> scopes = new string[] { $"{ResourceId}/.default" };
        private TokenResult tokenResult;

        [SetUp]
        public void Setup()
        {
            (this.logger, _) = MemoryLogger.Create();

            // MSAL Mocks
            this.testAccount = new Mock<IAccount>(MockBehavior.Strict);

            this.pcaWrapperMock = new Mock<IPCAWrapper>(MockBehavior.Strict);

            // Mock successful token result
            this.tokenResult = new TokenResult(new JsonWebToken(TokenResultTest.FakeToken), Guid.NewGuid());
        }

        [TearDown]
        public void Teardown()
        {
            this.pcaWrapperMock.VerifyAll();
            this.testAccount.VerifyAll();
        }

        public AuthFlow.IntegratedWindowsAuthentication Subject() => new AuthFlow.IntegratedWindowsAuthentication(this.logger, ClientId, TenantId, this.scopes, pcaWrapper: this.pcaWrapperMock.Object);

        [Test]
        public async Task CachedAuthSuccess()
        {
            this.MockAccount();
            this.CachedAuthResult();

            // Act
            AuthFlow.IntegratedWindowsAuthentication iwa = this.Subject();
            var authFlowResult = await iwa.GetTokenAsync();

            // Assert
            authFlowResult.TokenResult.Should().Be(this.tokenResult);
            authFlowResult.TokenResult.IsSilent.Should().BeTrue();
            authFlowResult.Errors.Should().BeEmpty();
            authFlowResult.AuthFlowName.Should().Be("iwa");
        }

        [Test]
        public async Task GetCachedToken_ReturnsNull()
        {
            this.MockAccount();
            this.CachedAuthReturnsNull();
            this.IWAReturnsResult();

            // Act
            AuthFlow.IntegratedWindowsAuthentication iwa = this.Subject();
            var authFlowResult = await iwa.GetTokenAsync();

            // Assert
            authFlowResult.TokenResult.Should().Be(this.tokenResult);
            authFlowResult.Errors.Should().BeEmpty();
            authFlowResult.AuthFlowName.Should().Be("iwa");
        }

        [Test]
        public void General_Exceptions_Are_ReThrown()
        {
            var message = "Something somwhere has gone terribly wrong!";
            this.MockAccount();
            this.pcaWrapperMock
                .Setup((pca) => pca.GetTokenSilentAsync(this.scopes, this.testAccount.Object, It.IsAny<CancellationToken>()))
                .Throws(new Exception(message));

            // Act
            AuthFlow.IntegratedWindowsAuthentication iwa = this.Subject();
            Func<Task> subject = async () => await iwa.GetTokenAsync();

            // Assert
            subject.Should().ThrowExactlyAsync<Exception>().WithMessage(message);
        }

        [Test]
        public async Task CachedAuth_Throws_ServiceException()
        {
            this.MockAccount();
            this.CachedAuthServiceException();

            // Act
            AuthFlow.IntegratedWindowsAuthentication iwa = this.Subject();
            var authFlowResult = await iwa.GetTokenAsync();

            // Assert
            authFlowResult.TokenResult.Should().Be(null);
            authFlowResult.Errors.Should().HaveCount(1);
            authFlowResult.Errors[0].Should().BeOfType(typeof(MsalServiceException));
            authFlowResult.AuthFlowName.Should().Be("iwa");
        }

        [Test]
        public async Task GetTokenSilent_OperationCanceledException()
        {
            this.MockAccount();
            this.CachedAuthTimeout();
            this.IWAReturnsResult();

            // Act
            AuthFlow.IntegratedWindowsAuthentication iwa = this.Subject();
            var authFlowResult = await iwa.GetTokenAsync();

            // Assert
            authFlowResult.TokenResult.Should().Be(this.tokenResult);
            authFlowResult.Errors.Should().HaveCount(1);
            authFlowResult.Errors[0].Should().BeOfType(typeof(AuthenticationTimeoutException));
            authFlowResult.Errors[0].Message.Should().Be("Get Token Silent timed out after 00:00:30");
            authFlowResult.AuthFlowName.Should().Be("iwa");
        }

        [Test]
        public async Task GetTokenSilent_MsalClientException()
        {
            this.MockAccount();
            this.CachedAuthClientException();

            // Act
            AuthFlow.IntegratedWindowsAuthentication iwa = this.Subject();
            var authFlowResult = await iwa.GetTokenAsync();

            // Assert
            authFlowResult.TokenResult.Should().Be(null);
            authFlowResult.Errors.Should().HaveCount(1);
            authFlowResult.Errors[0].Should().BeOfType(typeof(MsalClientException));
            authFlowResult.AuthFlowName.Should().Be("iwa");
        }

        [Test]
        public async Task GetTokenSilent_NullReferenceException()
        {
            this.MockAccount();
            this.CachedAuthNullReferenceException();

            // Act
            AuthFlow.IntegratedWindowsAuthentication iwa = this.Subject();
            var authFlowResult = await iwa.GetTokenAsync();

            // Assert
            authFlowResult.TokenResult.Should().Be(null);
            authFlowResult.Errors.Should().HaveCount(1);
            authFlowResult.Errors[0].Should().BeOfType(typeof(NullReferenceException));
            authFlowResult.AuthFlowName.Should().Be("iwa");
        }

        [Test]
        public async Task NoCachedAccounts_IWASuccess()
        {
            this.MockAccountReturnsNull();
            this.IWAReturnsResult();

            // Act
            AuthFlow.IntegratedWindowsAuthentication iwa = this.Subject();
            var authFlowResult = await iwa.GetTokenAsync();

            // Assert
            authFlowResult.TokenResult.Should().Be(this.tokenResult);
            authFlowResult.TokenResult.IsSilent.Should().BeTrue();
            authFlowResult.Errors.Should().BeEmpty();
            authFlowResult.AuthFlowName.Should().Be("iwa");
        }

        [Test]
        public async Task GetTokenIWA_ReturnsNull()
        {
            this.MockAccountReturnsNull();
            this.IWAReturnsNull();

            // Act
            AuthFlow.IntegratedWindowsAuthentication iwa = this.Subject();
            var authFlowResult = await iwa.GetTokenAsync();

            // Assert
            authFlowResult.TokenResult.Should().Be(null);
            authFlowResult.Errors.Should().BeEmpty();
            authFlowResult.AuthFlowName.Should().Be("iwa");
        }

        [Test]
        public async Task GetTokenIWA_MsalUIRequired_2FA()
        {
            this.MockAccountReturnsNull();
            this.IWAUIRequiredFor2FA();

            // Act
            AuthFlow.IntegratedWindowsAuthentication iwa = this.Subject();
            var authFlowResult = await iwa.GetTokenAsync();

            // Assert
            authFlowResult.TokenResult.Should().Be(null);
            authFlowResult.Errors.Should().HaveCount(1);
            authFlowResult.Errors[0].Should().BeOfType(typeof(MsalUiRequiredException));
            authFlowResult.Errors[0].Message.Should().Be("AADSTS50076 MSAL UI Required Exception!");
            authFlowResult.AuthFlowName.Should().Be("iwa");
        }

        [Test]
        public async Task GetTokenIWA_GenericMsalUIRequired()
        {
            this.MockAccountReturnsNull();
            this.IWAGenericUIRequiredException();

            // Act
            AuthFlow.IntegratedWindowsAuthentication iwa = this.Subject();
            var authFlowResult = await iwa.GetTokenAsync();

            // Assert
            authFlowResult.TokenResult.Should().Be(null);
            authFlowResult.Errors.Should().HaveCount(1);
            authFlowResult.Errors[0].Should().BeOfType(typeof(MsalUiRequiredException));
            authFlowResult.Errors[0].Message.Should().Be("MSAL UI Required Exception!");
            authFlowResult.AuthFlowName.Should().Be("iwa");
        }

        [Test]
        public async Task GetTokenIWA_MsalServiceException()
        {
            this.MockAccountReturnsNull();
            this.IWAServiceException();

            // Act
            AuthFlow.IntegratedWindowsAuthentication iwa = this.Subject();
            var authFlowResult = await iwa.GetTokenAsync();

            // Assert
            authFlowResult.TokenResult.Should().Be(null);
            authFlowResult.Errors.Should().HaveCount(1);
            authFlowResult.Errors[0].Should().BeOfType(typeof(MsalServiceException));
            authFlowResult.AuthFlowName.Should().Be("iwa");
        }

        [Test]
        public async Task GetTokenIWA_MsalClientException()
        {
            this.MockAccountReturnsNull();
            this.IWAClientException();

            // Act
            AuthFlow.IntegratedWindowsAuthentication iwa = this.Subject();
            var authFlowResult = await iwa.GetTokenAsync();

            // Assert
            authFlowResult.TokenResult.Should().Be(null);
            authFlowResult.Errors.Should().HaveCount(1);
            authFlowResult.Errors[0].Should().BeOfType(typeof(MsalClientException));
            authFlowResult.AuthFlowName.Should().Be("iwa");
        }

        private void CachedAuthResult()
        {
            this.pcaWrapperMock
               .Setup((pca) => pca.GetTokenSilentAsync(this.scopes, this.testAccount.Object, It.IsAny<CancellationToken>()))
               .ReturnsAsync(this.tokenResult);
        }

        private void CachedAuthReturnsNull()
        {
            this.pcaWrapperMock
               .Setup((pca) => pca.GetTokenSilentAsync(this.scopes, this.testAccount.Object, It.IsAny<CancellationToken>()))
               .ReturnsAsync((TokenResult)null);
        }

        private void CachedAuthServiceException()
        {
            this.pcaWrapperMock
                .Setup((pca) => pca.GetTokenSilentAsync(this.scopes, this.testAccount.Object, It.IsAny<CancellationToken>()))
                .Throws(new MsalServiceException(MsalServiceExceptionErrorCode, MsalServiceExceptionMessage));
        }

        private void CachedAuthTimeout()
        {
            this.pcaWrapperMock
                .Setup((pca) => pca.GetTokenSilentAsync(this.scopes, this.testAccount.Object, It.IsAny<CancellationToken>()))
                .Throws(new OperationCanceledException());
        }

        private void CachedAuthClientException()
        {
            this.pcaWrapperMock
                .Setup((pca) => pca.GetTokenSilentAsync(this.scopes, this.testAccount.Object, It.IsAny<CancellationToken>()))
                .Throws(new MsalClientException("1", "Could not find a WAM account for the silent request."));
        }

        private void CachedAuthNullReferenceException()
        {
            this.pcaWrapperMock
                .Setup((pca) => pca.GetTokenSilentAsync(this.scopes, this.testAccount.Object, It.IsAny<CancellationToken>()))
                .Throws(new NullReferenceException("There was a null reference excpetion. This should absolutly never happen and if it does it is a bug."));
        }

        private void IWAReturnsResult()
        {
            this.pcaWrapperMock
               .Setup((pca) => pca.GetTokenIntegratedWindowsAuthenticationAsync(this.scopes, It.IsAny<CancellationToken>()))
               .ReturnsAsync(this.tokenResult);
        }

        private void IWAReturnsNull()
        {
            this.pcaWrapperMock
               .Setup((pca) => pca.GetTokenIntegratedWindowsAuthenticationAsync(this.scopes, It.IsAny<CancellationToken>()))
               .ReturnsAsync((TokenResult)null);
        }

        private void IWAUIRequiredFor2FA()
        {
            this.pcaWrapperMock
                .Setup((pca) => pca.GetTokenIntegratedWindowsAuthenticationAsync(this.scopes, It.IsAny<CancellationToken>()))
                .Throws(new MsalUiRequiredException("1", "AADSTS50076 MSAL UI Required Exception!"));
        }

        private void IWAGenericUIRequiredException()
        {
            this.pcaWrapperMock
                .Setup((pca) => pca.GetTokenIntegratedWindowsAuthenticationAsync(this.scopes, It.IsAny<CancellationToken>()))
                .Throws(new MsalUiRequiredException("2", "MSAL UI Required Exception!"));
        }

        private void IWAServiceException()
        {
            this.pcaWrapperMock
                .Setup((pca) => pca.GetTokenIntegratedWindowsAuthenticationAsync(this.scopes, It.IsAny<CancellationToken>()))
                .Throws(new MsalServiceException(MsalServiceExceptionErrorCode, MsalServiceExceptionMessage));
        }

        private void IWAClientException()
        {
            this.pcaWrapperMock
                .Setup((pca) => pca.GetTokenIntegratedWindowsAuthenticationAsync(this.scopes, It.IsAny<CancellationToken>()))
                .Throws(new MsalClientException("1", "Could not find a WAM account for the silent request."));
        }

        private void MockAccount()
        {
            this.testAccount.Setup(a => a.Username).Returns(TestUser);
            this.pcaWrapperMock
                .Setup(pca => pca.TryToGetCachedAccountAsync(It.IsAny<string>()))
                .ReturnsAsync(this.testAccount.Object);
        }

        private void MockAccountReturnsNull()
        {
            this.pcaWrapperMock
                .Setup(pca => pca.TryToGetCachedAccountAsync(It.IsAny<string>()))
                .ReturnsAsync((IAccount)null);
        }
    }
}
