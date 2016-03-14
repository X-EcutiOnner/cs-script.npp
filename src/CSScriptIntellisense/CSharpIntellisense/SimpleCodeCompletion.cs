using Intellisense.Common;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CSScriptIntellisense
{
    public static class SimpleCodeCompletion
    {
        public static void Init()
        {
            MonoCompletionEngine.Init();
            RoslynCompletionEngine.Init();
        }

        static MonoCompletionEngine monoEngine = new MonoCompletionEngine();
        static RoslynCompletionEngine roslynEngine = new RoslynCompletionEngine();

        public static char[] Delimiters = "\\\t\n\r .,:;'\"[]{}()-/!?@$%^&*��><#|~`".ToCharArray();
        public static char[] CSS_Delimiters = "\\\t\n\r .,:;'\"[]{}()-!?@$%^&*��><#|~`".ToCharArray();
        static char[] lineDelimiters = new char[] { '\n', '\r' };

        static bool useRoslyn = false;

        static IEnumerable<ICompletionData> GetCSharpScriptCompletionData(string editorText, int offset)
        {
            var directiveLine = GetCSharpScriptDirectiveLine(editorText, offset);

            if (directiveLine.StartsWith("//css_")) //e.g. '//css_ref'
            {
                var word = Npp.GetWordAtPosition(offset); //e.g. 'css_ref'

                if (word.StartsWith("css_")) //directive itself
                {
                    return CssCompletionData.AllDirectives;
                }
                else //directive is complete and user is typing the next word (directive argument)
                {
                    if (directiveLine.StartsWith("//css_ref"))
                        return CssCompletionData.DefaultRefAsms;
                }
            }

            return null;
        }

        public static IEnumerable<ICompletionData> GetCompletionData(string editorText, int offset, string fileName, bool isControlSpace = true) // not the best way to put in the whole string every time
        {
            try
            {
                if (string.IsNullOrEmpty(editorText))
                    return new ICompletionData[0];

                return GetCSharpScriptCompletionData(editorText, offset) ??
                       (useRoslyn ?
                            roslynEngine.GetCompletionData(editorText, offset, fileName, isControlSpace) :
                            monoEngine.GetCompletionData(editorText, offset, fileName, isControlSpace));
            }
            catch
            {
                return new ICompletionData[0]; //the exception can happens even for the internal NRefactor-related reasons
            }
        }

        //----------------------------------
        public static void ResetProject(Tuple<string, string>[] sourceFiles = null, params string[] assemblies)
        {
            monoEngine.ResetProject(sourceFiles, assemblies);
        }

        //----------------------------------
        public static IEnumerable<TypeInfo> GetMissingUsings(string editorText, int offset, string fileName) // not the best way to put in the whole string every time
        {
            string nameToResolve = GetWordAt(editorText, offset);
            return GetPossibleNamespaces(editorText, nameToResolve, fileName);
        }

        internal static IEnumerable<TypeInfo> GetPossibleNamespaces(string editorText, string nameToResolve, string fileName) // not the best way to put in the whole string every time
        {
            return monoEngine.GetPossibleNamespaces(editorText, nameToResolve, fileName);
        }

        //----------------------------------
        public static string[] GetMemberInfo(string editorText, int offset, string fileName, bool collapseOverloads)
        {
            int methodStartPos;
            return GetMemberInfo(editorText, offset, fileName, collapseOverloads, out methodStartPos);
        }

        public static string[] GetMemberInfo(string editorText, int offset, string fileName, bool collapseOverloads, out int methodStartPos)
        {
            return monoEngine.GetMemberInfo(editorText, offset, fileName, collapseOverloads, out methodStartPos);
        }

        //----------------------------------
        static public string[] FindReferences(string editorText, int offset, string fileName)
        {
            return monoEngine.FindReferences(editorText, offset, fileName);
        }

        //----------------------------------
        static public ICSharpCode.NRefactory.TypeSystem.DomRegion ResolveMember(string editorText, int offset, string fileName)
        {
            return ResolveCSharpScriptMember(editorText, offset) ?? ResolveCSharpMember(editorText, offset, fileName);
        }

        static ICSharpCode.NRefactory.TypeSystem.DomRegion? ResolveCSharpScriptMember(string editorText, int offset)
        {
            var directiveLine = GetCSharpScriptDirectiveLine(editorText, offset);

            if (directiveLine.StartsWith("//css_"))
            {
                var css_directive = directiveLine.Split(SimpleCodeCompletion.CSS_Delimiters).FirstOrDefault();
                return CssCompletionData.ResolveDefinition(css_directive);
            }
            else
                return null;
        }

        static ICSharpCode.NRefactory.TypeSystem.DomRegion ResolveCSharpMember(string editorText, int offset, string fileName)
        {
            return monoEngine.ResolveCSharpMember(editorText, offset, fileName);
        }

        //----------------------------------

        internal static string GetWordAt(string editorText, int offset)
        {
            string retval = "";

            if (offset > 0 && editorText[offset - 1] != '.') //avoid "type.|"
            {
                //following VS default practice:  "type|."
                for (int i = offset - 1; i >= 0; i--)
                    if (SimpleCodeCompletion.Delimiters.Contains(editorText[i]))
                    {
                        retval = editorText.Substring(i + 1, offset - i - 1);
                        break;
                    }

                //extend the VS practice with the partial word support
                for (int i = offset; i < editorText.Length; i++)
                    if (SimpleCodeCompletion.Delimiters.Contains(editorText[i]))
                        break;
                    else
                        retval += editorText[i];
            }
            return retval;
        }

        internal static string GetPrevWordAt(string editorText, int offset)
        {
            string retval = "";

            int primaryWordStart = -1;

            if (offset > 0 && editorText[offset - 1] != '.') //avoid "type.|"
            {
                //following VS default practice:  "type|."
                for (int i = offset - 1; i >= 0; i--)
                    if (SimpleCodeCompletion.Delimiters.Contains(editorText[i]))
                    {
                        if (primaryWordStart == -1)
                        {
                            primaryWordStart = i;
                        }
                        else
                        {
                            retval = editorText.Substring(i + 1, primaryWordStart - i - 1).Trim();
                            break;
                        }
                    }
            }
            return retval;
        }

        static string GetCSharpScriptDirectiveLine(string editorText, int offset)
        {
            int i = 0;

            //need to allow 'space' as we are looking for a CS-Script line not a token
            var delimiters = SimpleCodeCompletion.CSS_Delimiters.Where(x => x != ' ');

            if (editorText[offset] != '.') //we may be at the partially complete word
                for (i = offset - 1; i >= 0; i--)
                    if (delimiters.Contains(editorText[i]))
                    {
                        offset = i + 1;
                        break;
                    }

            if (i == -1)
                offset = 0;

            var textOnRight = editorText.Substring(offset);
            var endPos = textOnRight.IndexOf('\n');
            if (endPos != -1)
                textOnRight = textOnRight.Substring(0, endPos - 1).TrimEnd('\r');
            return textOnRight;
        }
    }
}