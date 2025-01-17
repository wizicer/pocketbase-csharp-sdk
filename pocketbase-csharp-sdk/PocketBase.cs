﻿using pocketbase_csharp_sdk.Json;
using pocketbase_csharp_sdk.Models;
using pocketbase_csharp_sdk.Services;
using System.Collections;
using System.Collections.Specialized;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Mime;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Web;

namespace pocketbase_csharp_sdk
{
    public class PocketBase
    {
        public AuthStore AuthStore { private set; get; }
        public AdminService Admin { private set; get; }
        public UserService User { private set; get; }

        private readonly string _baseUrl;
        private readonly string _language;
        private readonly HttpClient _httpClient;

        public PocketBase(string baseUrl, AuthStore? authStore = null, string language = "en-US", HttpClient? httpClient = null)
        {
            this._baseUrl = baseUrl;
            this._language = language;
            this._httpClient = httpClient ?? new HttpClient();

            AuthStore = authStore ?? new AuthStore();
            Admin = new AdminService(this);
            User = new UserService(this);
        }

        public async Task<T?> SendAsync<T>(string path, HttpMethod method, IDictionary<string, string>? headers = null, IDictionary<string, object?>? query = null, IDictionary<string, object>? body = null, IEnumerable<FileContentWrapper>? files = null)
        {
            headers ??= new Dictionary<string, string>();
            query ??= new Dictionary<string, object?>();
            body ??= new Dictionary<string, object>();
            files ??= new List<FileContentWrapper>();

            Uri url = BuildUrl(path, query);

            HttpRequestMessage request;
            if (files.Count() > 0)
            {
                request = BuildFileRequest(method, url, headers, body, files);
            }
            else
            {
                request = BuildJsonRequest(method, url, headers, body);
            }

            if (!headers.ContainsKey("Authorization") && AuthStore.IsValid)
            {
                if (AuthStore.Model is AdminModel)
                {
                    request.Headers.Add("Authorization", $"Admin {AuthStore.Token}");
                }
                else
                {
                    request.Headers.Add("Authorization", $"User {AuthStore.Token}");
                }
            }

            if (!headers.ContainsKey("Accept-Language"))
            {
                request.Headers.Add("Accept-Language", _language);
            }

            try
            {
                var response = await _httpClient.SendAsync(request);
                //var responseData = await response.Content.ReadAsStringAsync();

                if ((int)response.StatusCode >= 400)
                {
                    //TODO
                    //var dic = GetResponseAsDictionary(responseData);
                    //throw new ClientException(url.ToString(), statusCode: (int)response.StatusCode, response: dic);
                    throw new ClientException(url.ToString(), statusCode: (int)response.StatusCode);
                }

                //if we dont care about the response, for example 204
                if (typeof(T) == typeof(object))
                {
                    return default;
                }

                return await response.Content.ReadFromJsonAsync<T>();
            }
            catch (Exception ex)
            {
                if (ex is ClientException)
                {
                    throw;
                }
                else if (ex is HttpRequestException)
                {
                    throw new ClientException(url: url.ToString(), originalError: ex, isAbort: true);
                }
                else
                {
                    throw new ClientException(url: url.ToString(), originalError: ex);
                }
            }
        }

        private IDictionary<string, object?>? GetResponseAsDictionary(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
            {
                return null;
            }
            var dic = new Dictionary<string, object?>();
            var obj = JsonDocument.Parse(response);
            foreach (var item in obj.RootElement.EnumerateObject())
            {
                
            }
            return dic;
        }

        public Uri BuildUrl(string path, IDictionary<string, object?>? queryParameters = null)
        {
            var url = _baseUrl + (_baseUrl.EndsWith("/") ? "" : "/");

            if (!string.IsNullOrWhiteSpace(path))
            {
                url += path.StartsWith("/") ? path.Substring(1) : path;
            }

            if (queryParameters is not null)
            {
                var query = NormalizeQueryParameters(queryParameters);

                List<string> urlSegments = new();
                foreach (var kvp in query)
                {
                    var encodedKey = HttpUtility.UrlEncode(kvp.Key);
                    foreach (var item in kvp.Value)
                    {
                        var encodedValue = HttpUtility.UrlEncode(item.ToString());
                        urlSegments.Add($"{encodedKey}={encodedValue}");
                    }
                }

                var queryString = string.Join("&", urlSegments);

                url = url + "?" + queryString;
            }

            return new Uri(url, UriKind.RelativeOrAbsolute);
        }

        private IDictionary<string, IEnumerable> NormalizeQueryParameters(IDictionary<string, object?>? parameters)
        {
            Dictionary<string, IEnumerable> result = new();

            if (parameters is null)
            {
                return result;
            }

            foreach (var item in parameters)
            {
                List<string> normalizedValue = new();
                IEnumerable valueAsList;

                if (item.Value is IEnumerable && item.Value is not string)
                {
                    valueAsList = (IEnumerable)item.Value;
                }
                else
                {
                    valueAsList = new List<object?>() { item.Value };
                }

                foreach (var subItem in valueAsList)
                {
                    if (subItem is null)
                    {
                        continue;
                    }
                    normalizedValue.Add(subItem.ToString() ?? "");
                }

                if (normalizedValue.Count > 0)
                {
                    result[item.Key] = normalizedValue;
                }
            }

            return result;
        }

        private HttpRequestMessage BuildJsonRequest(HttpMethod method, Uri url, IDictionary<string, string>? headers = null, IDictionary<string, object>? body = null)
        {
            var request = new HttpRequestMessage(method, url);
            request.Content = JsonContent.Create(body);

            if (headers is not null)
            {
                foreach (var header in headers)
                {
                    request.Headers.Add(header.Key, header.Value);
                }
            }

            return request;
        }

        private HttpRequestMessage BuildFileRequest(HttpMethod method, Uri url, IDictionary<string, string>? headers, IDictionary<string, object>? body, IEnumerable<FileContentWrapper> files)
        {
            var request = new HttpRequestMessage(method, url);

            //TODO Body beachten

            if (headers is not null)
            {
                foreach (var header in headers)
                {
                    request.Headers.Add(header.Key, header.Value);
                }
            }

            var form = new MultipartFormDataContent();
            foreach (var file in files)
            {
                var fileContent = new StreamContent(file.Stream);
                fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("multipart/form-data");
                form.Add(fileContent, file.FileName);
            }

            return request;
        }

    }
}