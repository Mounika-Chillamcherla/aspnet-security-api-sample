﻿/* 
*  Copyright (c) Microsoft. All rights reserved. Licensed under the MIT license. 
*  See LICENSE in the source repository root for complete license information. 
*/

using System.Threading.Tasks;
using System.Web.Mvc;
using Microsoft.Graph;
using MicrosoftGraph_Security_API_Sample.Helpers;
using MicrosoftGraph_Security_API_Sample.Models;
using Resources;
using Newtonsoft.Json;
using System;
using System.Linq;
using System.Text;
using System.Web;
using System.Collections.Generic;

namespace MicrosoftGraph_Security_API_Sample.Controllers
{
    public class HomeController : Controller
    {
        GraphService graphService = new GraphService();

        private AlertFilter AlertFilters
        {
            get => Session["AlertFilters"] as AlertFilter;
            set => Session["AlertFilters"] = value;
        }

        private UpdateAlertModel UpdateAlertFilters
        {
            get => Session["UpdateAlertFilters"] as UpdateAlertModel;
            set => Session["UpdateAlertFilters"] = value;
        }

        private SubscriptionFilters SubscriptionFilters
        {
            get => Session["SubscriptionFilters"] as SubscriptionFilters;
            set => Session["SubscriptionFilters"] = value;
        }


        private AlertModel CurrentAlert
        {
            get => Session["CurrentAlert"] as AlertModel;
            set => Session["CurrentAlert"] = value;
        }

        private string[] ProviderList
        {
            get => Session["ProviderList"] as string[];
            set => Session["ProviderList"] = value;
        }

        public async Task<ActionResult> Index()
        {
            if (!Request.IsAuthenticated)
            {
                Session["ProviderList"] = new[] { "All" };
            }
            else
            {
                Session["ProviderList"] = await GetProviderList();
            }               
            return View("Graph");
        }

        protected override void Initialize(System.Web.Routing.RequestContext requestContext)
        {
            base.Initialize(requestContext);
            Session["AlertFilters"] = Session["AlertFilters"] as AlertFilter ?? new AlertFilter { Top = 1 };
           

        }

        /// <summary>
        ///  Get the provider list
        /// </summary>
        /// <param name="alertFilter"></param>
        /// <returns></returns>
        public async Task<string[]> GetProviderList()
        {
            try
            {
                AlertFilter alertFilter = new AlertFilter { Top = 1 };
                
                var Top1Alerts = await graphService.GetAlerts(alertFilter);
                string[] providers = new string[Top1Alerts.Count+1];
                int index = 0;
                providers[index++] = "All";
                if (Top1Alerts != null)
                {
                    foreach(Alert alert in Top1Alerts)
                    {
                        providers[index++] = alert.VendorInformation.Provider;
                    }
                }

                ViewBag.ProviderList = providers;
                return providers;

            }
            catch(Exception ex)
            {

            }
            return null;
        }

        /// <summary>
        ///  Get the alerts based on filters
        /// </summary>
        /// <param name="alertFilter"></param>
        /// <returns></returns>
        [Authorize]
        public async Task<ActionResult> GetAlerts(AlertFilter alertFilter)
        {
            try
            {
                Session["CurrentAlert"] = null;
                ISecurityAlertsCollectionPage securityAlerts = await graphService.GetAlerts(alertFilter);

                var queryBuilder = new StringBuilder();
                queryBuilder.Append("SDK query: 'await graphClient.Security.Alerts.Request()");
                if (!string.IsNullOrEmpty(alertFilter.FilteredQuery))
                {
                    queryBuilder.Append($".Filter(\"{alertFilter.FilteredQuery}\")");
                }
                queryBuilder.Append($".Top({alertFilter.Top}).GetAsync()'");
                queryBuilder.Append("<br />");

               if (!string.IsNullOrEmpty(alertFilter.FilteredQuery))
                {
                    queryBuilder.Append($"REST query: '<a href=\"https://developer.microsoft.com/en-us/graph/graph-explorer?request=security/alerts?$filter={HttpUtility.UrlEncode(alertFilter.FilteredQuery)}%26$top={alertFilter.Top}&&method=GET&version=beta&GraphUrl=https://graph.microsoft.com\" target=\"_blank\">https://graph.microsoft.com/beta/security/alerts?");

                    queryBuilder.Append($"$filter={HttpUtility.UrlEncode(alertFilter.FilteredQuery)}&");
                    queryBuilder.Append($"$top={alertFilter.Top}</a>'");
                }
                else
                {
                    queryBuilder.Append($"REST query: '<a href=\"https://developer.microsoft.com/en-us/graph/graph-explorer?request=security/alerts?$top={alertFilter.Top}&&method=GET&version=beta&GraphUrl=https://graph.microsoft.com\" target=\"_blank\">https://graph.microsoft.com/beta/security/alerts?");
                    queryBuilder.Append($"$top={alertFilter.Top}</a>'");
                }
                queryBuilder.Append("<br />");

                var alertResultsModel = new AlertResultsModel
                {
                    Query = queryBuilder.ToString(),
                    Alerts = securityAlerts?.Select(sa => new AlertResultItemModel
                    {
                        Id = sa.Id,
                        Title = sa.Title,
                        Status = sa.Status,
                        Provider = sa.VendorInformation?.Provider,
                        AssignedTo = sa.AssignedTo,
                        Category = sa.Category
                    }) ?? Enumerable.Empty<AlertResultItemModel>()
                };

                Session["GetAlertResults"] = alertResultsModel;

                return View("Graph");
            }
            catch (ServiceException se)
            {
                if (se.Error.Message == Resource.Error_AuthChallengeNeeded)
                {
                    return new EmptyResult();
                }

                return RedirectToAction("Index", "Error", new { message = Resource.Error_Message + Request.RawUrl + ": " + se.Error.Message });
            }
        }

        /// <summary>
        /// Updates specific fields of alert
        /// </summary>
        /// <param name="updateAlertModel"></param>
        /// <returns></returns>
        [Authorize]
        public async Task<ActionResult> UpdateAlert(UpdateAlertModel updateAlertModel)
        {
            try
            {
                Session["GetAlertResults"] = null;
                UpdateAlertFilters = updateAlertModel;
                var queryBuilder = new StringBuilder();

                queryBuilder.Append($"SDK query: 'await graphClient.Security.Alerts[\"{updateAlertModel.AlertId}\"].Request().UpdateAsync(updatedAlert)'");
                queryBuilder.Append("<br />");

                queryBuilder.Append($"REST query: PATCH '<a>https://graph.microsoft.com/beta/security/alerts/{updateAlertModel.AlertId}</a>'");
                queryBuilder.Append("<br />");

                var email = await graphService.GetMyEmailAddress();
                if (!string.IsNullOrEmpty(email))
                {
                    email = $" alert.AssignedTo = {email}; ";
                }

                queryBuilder.Append($"Request Body: alert.Status = {updateAlertModel.UpdateStatus}; {email} alert.Feedback = {updateAlertModel.Feedback}; alert.Comments = {updateAlertModel.Comments} ");

                var updateAlertResultModel = new UpdateAlertResultModel { Query = queryBuilder.ToString() };

                if (string.IsNullOrEmpty(updateAlertModel.AlertId))
                {
                    updateAlertResultModel.Error = "Please enter valid Alert Id";
                    ViewBag.UpdateAlertResults = updateAlertResultModel;

                    return View("Graph");
                }

                Alert alert = await graphService.GetAlertById(updateAlertModel.AlertId);
                if (alert == null)
                {
                    updateAlertResultModel.Error = $"No alert matching this ID {updateAlertModel.AlertId} was found";
                    ViewBag.UpdateAlertResults = updateAlertResultModel;

                    return View("Graph");
                }

                updateAlertResultModel.Before = new UpdateAlertResultItemModel
                {
                    Title = alert.Title,
                    Status = alert.Status.ToString(),
                    Comments = alert.Comments,
                    Feedback = alert.Feedback.ToString(),
                    AssignedTo = alert.AssignedTo,
                    Category = alert.Category,
                    Provider = alert.VendorInformation.Provider,
                    Severity = alert.Severity
                };

                await graphService.UpdateAlert(alert, updateAlertModel);
                Alert alertUpdated = await graphService.GetAlertById(alert.Id);
                updateAlertResultModel.After = new UpdateAlertResultItemModel
                {
                    Title = alertUpdated.Title,
                    Status = alertUpdated.Status.ToString(),
                    Comments = alertUpdated.Comments,
                    Feedback = alertUpdated.Feedback.ToString(),
                    AssignedTo = alertUpdated.AssignedTo,
                    Category = alertUpdated.Category,
                    Provider = alertUpdated.VendorInformation.Provider,
                    Severity = alertUpdated.Severity
                };
               
                ViewBag.UpdateAlertResults = updateAlertResultModel;

                return View("Graph");
            }
            catch (ServiceException se)
            {
                if (se.Error.Message == Resource.Error_AuthChallengeNeeded)
                {
                    return new EmptyResult();
                }

                return RedirectToAction("Index", "Error", new { message = Resource.Error_Message + Request.RawUrl + ": " + se.Error.Message });
            }
            catch (Exception ex)
            {
                return RedirectToAction("Index", "Error", new { message = Resource.Error_Message + Request.RawUrl + ": " + ex.Message });
            }
        }
   
        /// <summary>
        /// Gets the device details which helps in further investigation of alert
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        [Authorize]
        public async Task<ActionResult> GetDevice(string id)
        {
            if (CurrentAlert?.User != null)
            {
                CurrentAlert.User.SelectedDevice = await graphService.GetDeviceById(id);
            }

            return View("Graph");
        }

        /// <summary>
        /// Gets the alert by alertid
        /// </summary>
        /// <param name="id">Id of the alert</param>
        /// <returns></returns>
        [Authorize]
        public async Task<ActionResult> GetAlert(string id)
        {
            try
            {
                var alert = await graphService.GetAlertById(id);

                if (alert == null)
                {
                    CurrentAlert = new AlertModel { Metadata = $"No alert matching this ID {id} was found" };
                    return View("Graph");
                }

                var queryBuilder = new StringBuilder();
                queryBuilder.Append($"SDK query: 'await graphClient.Security.Alerts[\"{id}\"].Request().GetAsync();'");
                queryBuilder.Append("<br />");

                queryBuilder.Append($"REST query: '<a href=\"https://developer.microsoft.com/en-us/graph/graph-explorer?request=security/alerts/{id}&method=GET&version=beta&GraphUrl=https://graph.microsoft.com\" target=\"_blank\">https://graph.microsoft.com/beta/security/alerts/{id}/</a>'");
                queryBuilder.Append("<br />");


                var alertModel = new AlertModel
                {
                    Id = alert.Id,
                    Metadata = JsonConvert.SerializeObject(alert, Formatting.Indented),
                    Query = queryBuilder.ToString(),
                    Comments = alert.Comments,
                    Status = alert.Status.ToString(),
                    Feedback = alert.Feedback.ToString()
                };

                var principalName = alert.UserStates?.FirstOrDefault()?.UserPrincipalName;
                if (!string.IsNullOrEmpty(principalName))
                {
                    alertModel.User = await graphService.GetUserDetails(principalName, populatePicture: true, populateManager: true, populateDevices: true);
                }

                var hostState = alert.HostStates?.FirstOrDefault();
                if (hostState != null)
                {
                    alertModel.Device = new AlertDeviceModel
                    {
                        Fqdn = hostState.Fqdn,
                        IsAzureDomainJoined = hostState.IsAzureAadJoined,
                        PublicIpAddress = hostState.PublicIpAddress,
                        PrivateIpAddress = hostState.PrivateIpAddress
                    };
                }

                CurrentAlert = alertModel;

                return View("Graph");
            }
            catch (ServiceException se)
            {
                if (se.Error.Message == Resource.Error_AuthChallengeNeeded)
                {
                    return new EmptyResult();
                }

                return RedirectToAction("Index", "Error",
                    new { message = Resource.Error_Message + Request.RawUrl + ": " + se.Error.Message });
            }
        }

        /// <summary>
        /// Create the subscription based on the subscription filters
        /// </summary>
        /// <param name="subscriptionFilters"></param>
        /// <returns></returns>
        [Authorize]
        public async Task<ActionResult> Subscribe(SubscriptionFilters subscriptionFilters)
        {
            Session["CurrentAlert"] = null;
            SubscriptionFilters = subscriptionFilters;
            try
            {
                var subscription = await graphService.Subscribe(subscriptionFilters);
                var queryBuilder = new StringBuilder();

                queryBuilder.Append($"SDK query: 'graphClient.Subscriptions.Request().AddAsync(subscription)'");
                queryBuilder.Append("<br />");

                queryBuilder.Append($"REST query: POST '<a>https://graph.microsoft.com/beta/subscriptions</a>'");
                queryBuilder.Append("<br />");
               
                //var updateAlertResultModel = new UpdateAlertResultModel { Query = queryBuilder.ToString() };


                if (subscription!= null)
                {
                    var subscriptionResultModel = new SubscriptionResultModel()
                    {
                        Id = subscription.Id,
                        Resource = subscription.Resource,
                        NotificationUrl = subscription.NotificationUrl,
                        ExpirationDateTime = subscription.ExpirationDateTime,
                        ChangeType = subscription.ChangeType,
                        ClientState = subscription.ClientState
                    };
                    ViewBag.GetSubscriptionResults = subscriptionResultModel;
                }
                else
                {
                    ViewBag.GetSubscriptionResults = null;

                }
                Session["GetAlertResults"] = null;

                return View("Graph");
            }
            catch (ServiceException se)
            {
                if (se.Error.Message == Resource.Error_AuthChallengeNeeded)
                {
                    return new EmptyResult();
                }

                return RedirectToAction("Index", "Error", new { message = Resource.Error_Message + Request.RawUrl + ": " + se.Error.Message });
            }
        }


        public ActionResult About()
        {
            return View();
        }
    }
}