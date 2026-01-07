using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using System.IO;
using System.Threading.Tasks;

namespace qlthucung.Helpers
{
    public interface IViewRenderService
    {
        Task<string> RenderToStringAsync(Controller controller, string viewName, object model);
    }

    public class ViewRenderService : IViewRenderService
    {
        private readonly ICompositeViewEngine _viewEngine;
        private readonly ITempDataProvider _tempDataProvider;

        public ViewRenderService(ICompositeViewEngine viewEngine, ITempDataProvider tempDataProvider)
        {
            _viewEngine = viewEngine;
            _tempDataProvider = tempDataProvider;
        }

        public async Task<string> RenderToStringAsync(Controller controller, string viewName, object model)
        {
            var httpContext = controller.HttpContext;
            using (var sw = new StringWriter())
            {
                var actionContext = new ActionContext(httpContext, controller.RouteData, controller.ControllerContext.ActionDescriptor);

                var viewResult = _viewEngine.FindView(actionContext, viewName, false);
                if (viewResult.View == null)
                {
                    throw new FileNotFoundException($"View '{viewName}' không tìm thấy.");
                }

                var viewDictionary = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
                {
                    Model = model
                };

                var viewContext = new ViewContext(
                    actionContext,
                    viewResult.View,
                    viewDictionary,
                    new TempDataDictionary(httpContext, _tempDataProvider),
                    sw,
                    new HtmlHelperOptions()
                );

                await viewResult.View.RenderAsync(viewContext);
                return sw.ToString();
            }
        }
    }
}