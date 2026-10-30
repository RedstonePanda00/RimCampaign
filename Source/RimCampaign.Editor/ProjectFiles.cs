using System;
using System.IO;
using System.Runtime.Serialization;
using RimCampaign.Core;

namespace RimCampaign.Editor
{
    public static class ProjectFiles
    {
        public static string ResolvePath(string input,bool reading) {
            string path=(input??"").Trim().Trim('"');
            if(string.IsNullOrWhiteSpace(path))throw new ArgumentException(RimCampaign.Runtime.RcText.T("JSON 路径错误：请输入文件的完整路径。"));
            try{path=Path.GetFullPath(path);}catch(Exception ex) when(ex is ArgumentException||ex is NotSupportedException||ex is PathTooLongException){
                throw new ArgumentException(RimCampaign.Runtime.RcText.T("JSON 路径错误：路径格式无效。\n")+path);
            }
            if(Directory.Exists(path))throw new ArgumentException(RimCampaign.Runtime.RcText.T("JSON 路径错误：选择的是文件夹，请指定其中的 .json 文件。\n")+path);
            if(!string.Equals(Path.GetExtension(path),".json",StringComparison.OrdinalIgnoreCase)&&!Json.CompressedPath(path))throw new ArgumentException(RimCampaign.Runtime.RcText.T("JSON 路径错误：文件扩展名必须为 .json 或 .json.gz。\n")+path);
            if(reading&&!File.Exists(path))throw new FileNotFoundException(RimCampaign.Runtime.RcText.T("JSON 路径错误：找不到文件，请检查文件名和所在目录。\n")+path,path);
            return path;
        }
        public static Project Load(string input) {
            var path=ResolvePath(input,true);
            try{
                var project=Json.Load<Project>(path);
                if(project?.Maps==null||project.Maps.Count==0)throw new SerializationException(RimCampaign.Runtime.RcText.T("工程没有地图。"));
                return project;
            }catch(Exception ex) when(ex is SerializationException||ex is InvalidDataException){throw new ArgumentException(RimCampaign.Runtime.RcText.T("JSON 内容错误：该文件不是有效的战役工程。\n")+path+"\n"+ex.Message);}
            catch(UnauthorizedAccessException){throw new ArgumentException(RimCampaign.Runtime.RcText.T("JSON 文件访问失败：没有读取权限。\n")+path);}
            catch(IOException ex){throw new IOException(RimCampaign.Runtime.RcText.T("JSON 文件读取失败：\n")+path+"\n"+ex.Message,ex);}
        }
        public static void Save(string input,Project project) {
            var path=ResolvePath(input,false);
            try{Json.Save(path,project);}
            catch(Exception ex) when(ex is IOException||ex is UnauthorizedAccessException){throw new IOException(RimCampaign.Runtime.RcText.T("JSON 文件保存失败：请检查目录权限、文件占用和磁盘空间。\n")+path+"\n"+ex.Message,ex);}
        }
    }
}
