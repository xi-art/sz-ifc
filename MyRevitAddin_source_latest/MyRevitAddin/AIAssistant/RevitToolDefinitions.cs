using Autodesk.Revit.DB;
using System.Collections.Generic;

namespace MyRevitAddin.AIAssistant
{
    /// <summary>
    /// Revit 操作工具定义（告诉 AI 可以调用哪些函数）
    /// 使用纯 Dictionary，兼容 JavaScriptSerializer
    /// </summary>
    public static class RevitToolDefinitions
    {
        public static List<Dictionary<string, object>> GetAllTools()
        {
            var tools = new List<Dictionary<string, object>>();

            tools.Add(MakeTool(
                "get_selected_elements",
                "获取当前 Revit 文档中选中的所有图元（元素）。返回元素类型、ID、名称等信息。",
                new Dictionary<string, object> { { "type", "object" }, { "properties", new Dictionary<string, object>() } }
            ));

            tools.Add(MakeTool(
                "get_document_info",
                "获取当前打开的 Revit 文档（项目文件）的基本信息，包括文档标题、文件路径、是否已保存、作者等。",
                new Dictionary<string, object> { { "type", "object" }, { "properties", new Dictionary<string, object>() } }
            ));

            tools.Add(MakeTool(
                "get_all_levels",
                "获取项目中所有标高（Level）列表，返回标高名称、标高值（单位：米）、ID等信息。",
                new Dictionary<string, object> { { "type", "object" }, { "properties", new Dictionary<string, object>() } }
            ));

            tools.Add(MakeTool(
                "get_all_views",
                "获取项目中所有视图和图纸列表，包括平面视图、立面、剖面、3D视图、图纸（Sheet）等。可按类型过滤。",
                new Dictionary<string, object>
                {
                    { "type", "object" },
                    { "properties", new Dictionary<string, object>
                        {
                            { "view_type", new Dictionary<string, object> { { "type", "string" }, { "description", "可选：视图类型过滤，如 FloorPlan（平面）、ThreeD（3D）、DrawingSheet（图纸）、Section（剖面）、Elevation（立面）、All（全部，默认）" } } }
                        }
                    }
                }
            ));

            tools.Add(MakeTool(
                "select_elements_by_category",
                "按类别（Category）批量选择项目中的图元。例如：选择所有门、所有墙、所有管道。选择后可以继续通过其他工具操作。",
                new Dictionary<string, object>
                {
                    { "type", "object" },
                    { "properties", new Dictionary<string, object>
                        {
                            { "category_name", new Dictionary<string, object> { { "type", "string" }, { "description", "类别名称（中文或英文），例如：门、窗、墙、楼板、管道、设备、柱、梁、家具、机电设备" } } },
                            { "limit", new Dictionary<string, object> { { "type", "integer" }, { "description", "可选：最多返回的数量，默认 500，防止返回过多" } } }
                        }
                    },
                    { "required", new[] { "category_name" } }
                }
            ));

            tools.Add(MakeTool(
                "get_project_sheets",
                "获取项目中所有图纸（Sheet）列表，返回图纸编号、图纸名称、图纸上的视口（视图）数量等信息。",
                new Dictionary<string, object> { { "type", "object" }, { "properties", new Dictionary<string, object>() } }
            ));

            tools.Add(MakeTool(
                "set_instance_parameter",
                "修改一个或多个图元的实例参数值。可以批量操作。",
                new Dictionary<string, object>
                {
                    { "type", "object" },
                    { "properties", new Dictionary<string, object>
                        {
                            { "element_ids", new Dictionary<string, object> { { "type", "array" }, { "items", new Dictionary<string, object> { { "type", "integer" } } }, { "description", "要修改的图元 ID 列表" } } },
                            { "parameter_name", new Dictionary<string, object> { { "type", "string" }, { "description", "参数名称（中文或英文）" } } },
                            { "value", new Dictionary<string, object> { { "type", "string" }, { "description", "参数值（字符串形式）" } } }
                        }
                    },
                    { "required", new[] { "element_ids", "parameter_name", "value" } }
                }
            ));

            tools.Add(MakeTool(
                "batch_set_parameter",
                "根据规则批量设置参数。例如：把所有设备按所在楼层设置楼层参数。",
                new Dictionary<string, object>
                {
                    { "type", "object" },
                    { "properties", new Dictionary<string, object>
                        {
                            { "rule_type", new Dictionary<string, object> { { "type", "string" }, { "description", "规则类型：by_floor（按楼层）、by_category（按类别）" } } },
                            { "parameter_name", new Dictionary<string, object> { { "type", "string" }, { "description", "要设置的参数名称" } } }
                        }
                    },
                    { "required", new[] { "rule_type", "parameter_name" } }
                }
            ));

            tools.Add(MakeTool(
                "replace_family",
                "批量替换图元的族类型。例如：把所有 M1 门换成 M2 门。",
                new Dictionary<string, object>
                {
                    { "type", "object" },
                    { "properties", new Dictionary<string, object>
                        {
                            { "target_category", new Dictionary<string, object> { { "type", "string" }, { "description", "目标类别，例如：门、窗、家具" } } },
                            { "new_type_name", new Dictionary<string, object> { { "type", "string" }, { "description", "新族类型名称" } } }
                        }
                    },
                    { "required", new[] { "target_category", "new_type_name" } }
                }
            ));

            // ===== 记忆工具 =====
            tools.Add(MakeTool(
                "save_memory",
                "保存一条记忆到本地。当用户说'记住XXX'、'记一下XXX'时调用。保存后下次相同命令可以回忆起来。",
                new Dictionary<string, object>
                {
                    { "type", "object" },
                    { "properties", new Dictionary<string, object>
                        {
                            { "key", new Dictionary<string, object> { { "type", "string" }, { "description", "记忆关键词，例如：批量替换门的操作用法" } } },
                            { "content", new Dictionary<string, object> { { "type", "string" }, { "description", "要记住的内容" } } }
                        }
                    },
                    { "required", new[] { "key", "content" } }
                }
            ));

            tools.Add(MakeTool(
                "search_memory",
                "搜索本地记忆。当用户问'记得XXX吗'、'之前XXX是怎么做的'时调用。返回所有匹配的记忆。",
                new Dictionary<string, object>
                {
                    { "type", "object" },
                    { "properties", new Dictionary<string, object>
                        {
                            { "keyword", new Dictionary<string, object> { { "type", "string" }, { "description", "搜索关键词，例如：批量替换、设置楼层" } } }
                        }
                    },
                    { "required", new[] { "keyword" } }
                }
            ));

            return tools;
        }

        private static Dictionary<string, object> MakeTool(string name, string description, Dictionary<string, object> parameters)
        {
            return new Dictionary<string, object>
            {
                { "type", "function" },
                { "function", new Dictionary<string, object>
                    {
                        { "name", name },
                        { "description", description },
                        { "parameters", parameters }
                    }
                }
            };
        }
    }
}
