global using System.Globalization;
global using System.Net.Http.Json;
global using BikeBuilder.API.Protos;
global using BikeBuilder.Contracts.Authorization;
global using BikeBuilder.Contracts.Components;
global using BikeBuilder.Contracts.Types;
global using BikeBuilder.Web.Admin.Dialogs;
global using BikeBuilder.Web.Admin.Editors;
global using BikeBuilder.Web.Admin.Services;
global using Google.Protobuf.WellKnownTypes;
global using Grpc.Core;
global using Microsoft.AspNetCore.Components;
global using Microsoft.AspNetCore.Components.Forms;
global using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
global using MudBlazor;
// Google.Protobuf.WellKnownTypes also defines a Type; the editor registry means System.Type
// is the one this project talks about.
global using Type = System.Type;
