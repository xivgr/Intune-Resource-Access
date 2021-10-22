// Copyright (c) Microsoft Corporation.
// All rights reserved.
//
// This code is licensed under the MIT License.
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files(the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and / or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions :
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

namespace Microsoft.Intune
{
    /// <summary>
    /// API to validate SCEP Requests that were generated by Intune
    /// </summary>
    public class IntuneScepValidator
    {
        public const string DEFAULT_SERVICE_VERSION = "2018-02-20";
        public const string VALIDATION_SERVICE_NAME = "ScepRequestValidationFEService";
        public const string VALIDATION_URL = "ScepActions/validateRequest";
        public const string NOTIFY_SUCCESS_URL = "ScepActions/successNotification";
        public const string NOTIFY_FAILURE_URL = "ScepActions/failureNotification";

        private TraceSource trace = new TraceSource(nameof(IntuneScepValidator));

        /// <summary>
        /// The version of the ScepRequestValidationFEService that we are making requests against.
        /// </summary>
        private string serviceVersion = null;

        /// <summary>
        /// The CertificateAuthority Identifier to be used in log correlation on Intune.
        /// </summary>
        private string providerNameAndVersion = null;

        /// <summary>
        /// The Intune client to use to make requests to Intune services.
        /// </summary>
        private IIntuneClient intuneClient = null;

        /// <summary>
        /// Creates an new instance of IntuneScepValidator
        /// </summary>
        /// <param name="providerNameAndVersion">A string that uniquely identifies your Certificate Authority and any version info for your app.</param>
        /// <param name="azureAppId">Azure Active Directory application Id to use for authentication.</param>
        /// <param name="azureAppKey">Azure Active Directory application key to use for authentication.</param>
        /// <param name="intuneTenant">Tenant name of intune customer.</param>
        /// <param name="serviceVersion">Specific version of ScepRequestValidationFEService to make requests to.</param>
        /// <param name="intuneAppId">Application Id of Intune in graph.</param>
        /// <param name="intuneResourceUrl">Intune resources URL to request access to.</param>
        /// <param name="graphApiVersion">Specific version of graph to make requests to.</param>
        /// <param name="graphResourceUrl">Graph resource URL to request access to.</param>
        /// <param name="authAuthority">Authorization authority to use for requesting access to resources.</param>
        /// <param name="trace">Trace</param>
        /// <param name="intuneClient">IntuneClient to use to make requests to intune.</param>
        /// <param name="httpClient">HttpClient to use to make any http requests.</param>
        [SuppressMessage("Microsoft.Usage", "CA2208", Justification = "Using a parameter coming from an object.")]
        public IntuneScepValidator(
            Dictionary<string,string> configProperties,
            TraceSource trace = null,
            IIntuneClient intuneClient = null)
        {
            // Required Parameters
            if (configProperties == null)
            {
                throw new ArgumentNullException(nameof(configProperties));
            }

            configProperties.TryGetValue("PROVIDER_NAME_AND_VERSION", out this.providerNameAndVersion);
            if (string.IsNullOrWhiteSpace(providerNameAndVersion))
            {
                throw new ArgumentNullException(nameof(providerNameAndVersion));
            }

            // Optional Parameters
            if (trace != null)
            {
                this.trace = trace;
            }

            configProperties.TryGetValue("ScepRequestValidationFEServiceVersion", out this.serviceVersion);
            serviceVersion = serviceVersion ?? DEFAULT_SERVICE_VERSION;

            // Dependencies
            var authClient = new MsalClient(
                        // Required
                        configProperties,
                        // Overrides
                        trace: trace
                        );

            this.intuneClient = intuneClient ?? new IntuneClient(
                    // Required    
                    configProperties,
                    // Overrides
                    trace: trace,
                    // Dependencies
                    authClient: authClient,
                    locationProvider: new IntuneServiceLocationProvider(
                        // Required
                        configProperties,
                        // Overrides
                        trace: trace,
                        // Dependencies
                        authClient: authClient
                        )
                    );
        }

        /// <summary>
        /// Validates whether the given Certificate Request is a valid and from Microsoft Intune.
        /// If the request is not valid an exception will be thrown.
        /// 
        /// IMPORTANT: If an exception is thrown the SCEP server should not issue a certificate to the client.
        /// </summary>
        /// <param name="transactionId">The transactionId of the Certificate Request</param>
        /// <param name="certificateRequest">Base 64 encoded PKCS10 packet</param>
        /// <returns></returns>
        public async Task ValidateRequestAsync(string transactionId, string certificateRequest)
        {
            if (string.IsNullOrWhiteSpace(transactionId))
            {
                throw new ArgumentNullException(nameof(transactionId));
            }

            if (string.IsNullOrWhiteSpace(certificateRequest))
            {
                throw new ArgumentNullException(nameof(certificateRequest));
            }

            JObject requestBody = new JObject(
                new JProperty("request", new JObject( 
                    new JProperty("transactionId", transactionId),
                    new JProperty("certificateRequest", certificateRequest),
                    new JProperty("callerInfo", this.providerNameAndVersion))));

            await PostAsync(requestBody, VALIDATION_URL, transactionId);
        }

        /// <summary>
        /// Send a Success notification to the SCEP Service.
        /// 
        /// IMPORTANT: If an exception is thrown the SCEP server should not issue a certificate to the client.
        /// </summary>
        /// <param name="transactionId">The transactionId of the CSR</param>
        /// <param name="certificateRequest">Base 64 encoded PKCS10 packet</param>
        /// <param name="certThumbprint">Thumbprint of the certificate issued.</param>
        /// <param name="certSerialNumber">Serial number of the certificate issued.</param>
        /// <param name="certExpirationDate">The date time string should be formated as web UTC time (YYYY-MM-DDThh:mm:ss.sssTZD) ISO 8601. </param>
        /// <param name="certIssuingAuthority">Issuing Authority that issued the certificate.</param>
        /// <returns></returns>
        public async Task SendSuccessNotificationAsync(string transactionId, string certificateRequest, string certThumbprint, string certSerialNumber, string certExpirationDate, string certIssuingAuthority)
        {
            if (string.IsNullOrWhiteSpace(transactionId))
            {
                throw new ArgumentNullException(nameof(transactionId));
            }

            if (string.IsNullOrWhiteSpace(certificateRequest))
            {
                throw new ArgumentNullException(nameof(certificateRequest));
            }

            if (string.IsNullOrWhiteSpace(certThumbprint))
            {
                throw new ArgumentNullException(nameof(certThumbprint));
            }

            if (string.IsNullOrWhiteSpace(certSerialNumber))
            {
                throw new ArgumentNullException(nameof(certSerialNumber));
            }

            if (string.IsNullOrWhiteSpace(certExpirationDate))
            {
                throw new ArgumentNullException(nameof(certExpirationDate));
            }

            if (string.IsNullOrWhiteSpace(certIssuingAuthority))
            {
                throw new ArgumentNullException(nameof(certIssuingAuthority));
            }

            JObject requestBody = new JObject(
                new JProperty("notification", new JObject(
                    new JProperty("transactionId", transactionId),
                    new JProperty("certificateRequest", certificateRequest),
                    new JProperty("certificateThumbprint", certThumbprint),
                    new JProperty("certificateSerialNumber", certSerialNumber),
                    new JProperty("certificateExpirationDateUtc", certExpirationDate),
                    new JProperty("issuingCertificateAuthority", certIssuingAuthority),
                    new JProperty("callerInfo", this.providerNameAndVersion))));

            await PostAsync(requestBody, NOTIFY_SUCCESS_URL, transactionId);
        }

        /// <summary>
        /// Send a Failure notification to the SCEP service. 
        /// 
        /// IMPORTANT: If this method is called the SCEP server should not issue a certificate to the client.
        /// </summary>
        /// <param name="transactionId">The transactionId of the CSR</param>
        /// <param name="certificateRequest">Base 64 encoded PKCS10 packet</param>
        /// <param name="hResult">hResult 32-bit error code formulated using the instructions specified in https://msdn.microsoft.com/en-us/library/cc231198.aspx. 
        /// The value specified will be reported in the Intune management console and will be used by the administrator to troubleshoot the issue.
        /// It is recommended that your product provide documentation about the meaning of the error codes reported.</param>
        /// <param name="errorDescription">Description of what error occurred. Max length = 255 chars</param>
        /// <returns></returns>
        public async Task SendFailureNotificationAsync(string transactionId, string certificateRequest, long hResult, string errorDescription)
        {
            if (string.IsNullOrWhiteSpace(transactionId))
            {
                throw new ArgumentNullException(nameof(transactionId));
            }

            if (string.IsNullOrWhiteSpace(certificateRequest))
            {
                throw new ArgumentNullException(nameof(certificateRequest));
            }

            if (string.IsNullOrWhiteSpace(errorDescription))
            {
                throw new ArgumentNullException(nameof(errorDescription));
            }

            JObject requestBody = new JObject(
                new JProperty("notification", new JObject(
                    new JProperty("transactionId", transactionId),
                    new JProperty("certificateRequest", certificateRequest),
                    new JProperty("hResult", hResult),
                    new JProperty("errorDescription", errorDescription),
                    new JProperty("callerInfo", this.providerNameAndVersion))));

            await PostAsync(requestBody, NOTIFY_FAILURE_URL, transactionId);
        }

        private async Task PostAsync(JObject requestBody, string urlSuffix, string transactionId)
        {
            Guid activityId = Guid.NewGuid();
            JObject result = await intuneClient.PostAsync(VALIDATION_SERVICE_NAME,
                        urlSuffix,
                        serviceVersion,
                        requestBody,
                        activityId);

            trace.TraceEvent(TraceEventType.Information, 0, "Activity " + activityId + " has completed.");
            trace.TraceEvent(TraceEventType.Information, 0, result.ToString());

            string code = (string)result["code"];
            string errorDescription = (string)result["errorDescription"];

            IntuneScepServiceException e = new IntuneScepServiceException(code, errorDescription, transactionId, activityId, trace);

            if (e.ParsedErrorCode != IntuneScepServiceException.ErrorCode.Success)
            {
                trace.TraceEvent(TraceEventType.Warning, 0, e.Message);
                throw e;
            }
        }
    }
}