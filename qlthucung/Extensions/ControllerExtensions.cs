using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace qlthucung.Extensions
{
    public static class ControllerExtensions
    {
        public static async Task<string> RenderToStringAsync(this PartialViewResult partialView, HttpContext httpContext)
        {
            var serviceProvider = httpContext.RequestServices;
            var viewEngine = (ICompositeViewEngine)serviceProvider.GetService(typeof(ICompositeViewEngine));
            var tempDataProvider = (ITempDataProvider)serviceProvider.GetService(typeof(ITempDataProvider));

            using (var sw = new StringWriter())
            {
                var viewResult = viewEngine.FindView(new ActionContext(httpContext, new Microsoft.AspNetCore.Routing.RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor()), partialView.ViewName, false);

                if (viewResult.View == null)
                {
                    throw new FileNotFoundException($"Partial view {partialView.ViewName} not found.");
                }

                var viewDictionary = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
                {
                    Model = partialView.Model
                };

                var viewContext = new ViewContext(
                    new ActionContext(httpContext, new Microsoft.AspNetCore.Routing.RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor()),
                    viewResult.View,
                    viewDictionary,
                    new TempDataDictionary(httpContext, tempDataProvider),
                    sw,
                    new HtmlHelperOptions()
                );

                await viewResult.View.RenderAsync(viewContext);
                return sw.ToString();
            }
        }
    }
}
