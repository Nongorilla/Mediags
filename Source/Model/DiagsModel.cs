using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NongIssue;
using NongFormat;

namespace NongMediaDiags
{
    public partial class Diags
    {
        public partial class Model
        {
            protected virtual Diags _data { get; set; }
            public Diags Bind => _data;
            public FileFormat.Vector.Model FormatModel;

            public Model (string root, string filter=null, string exclusion=null,
                Interaction action=Interaction.None, Granularity scope=Granularity.Summary,
                IssueTags warnEscalator=IssueTags.None, IssueTags errEscalator=IssueTags.None)
                : this()
            {
                _data = new Diags (this);
                Bind.Root = root;
                Bind.Filter = filter;
                Bind.Exclusion = exclusion;
                Bind.Response = action;
                Bind.Scope = scope;
                Bind.WarnEscalator = warnEscalator;
                Bind.ErrEscalator = errEscalator;
            }


            protected Model()
            {
                FormatModel = new FileFormat.Vector.Model();
                LoadFormats();
            }


            // Interrogate the assembly for any classes to add to the list of file formats. They:
            // 1. Must be nested in a class that
            //    1a. ends with "Format"
            //    1b. derives from FormatBase
            //    1c. contains the method public static CreateModel (Stream, byte[], string)
            // 2. Must be named "Model" and derive from a FormatBase.ModelBase
            // 4. Must contain the property public static string[] Names { get; }
            // 5. May contain the property public static string Subname { get; }
#if NETFX_CORE
            private void LoadFormats()
            {
                foreach (var type in typeof (FormatBase).GetTypeInfo().Assembly.DefinedTypes)
                    if (type.IsClass && type.IsSubclassOf (typeof (FormatBase)) && type.Name.EndsWith ("Format"))
                    {
                        MethodInfo createrInfo = null, namesInfo = null, subnameInfo = null;
                        foreach (var meth in type.DeclaredMethods)
                            if ((meth.Attributes & MethodAttributes.Public|MethodAttributes.Static) == (MethodAttributes.Public|MethodAttributes.Static))
                            {
                                Type ret = meth.ReturnType;
                                if (! meth.IsSpecialName)
                                {
                                    var x2 = ret.Name;
                                    if (meth.Name == "CreateModel")
                                    {
                                        var parm = meth.GetParameters();
                                        var bt = ret.GetTypeInfo().BaseType;
                                        while (ret.GetTypeInfo().BaseType != typeof (object))
                                            ret = ret.GetTypeInfo().BaseType;
                                        if (ret == typeof (FormatBase.ModelBase) && parm.Length == 3
                                                && parm[0].ParameterType == typeof (Stream)
                                                && parm[1].ParameterType == typeof (byte[])
                                                && parm[2].ParameterType == typeof (string))
                                            createrInfo = meth;
                                    }
                                }
                                else if (meth.Name=="get_Names" && ret == typeof (string[]))
                                    namesInfo = meth;
                                else if (meth.IsSpecialName && meth.Name=="get_Subname" && ret == typeof (string))
                                    subnameInfo = meth;
                            }
                        if (namesInfo != null && createrInfo != null)
                        {
                            var factorySignature = new Type[] { typeof (Stream), typeof (byte[]), typeof (string) };
                            MethodInfo runInfo = RuntimeReflectionExtensions.GetRuntimeMethod (type.AsType(), "Create", factorySignature);
                            var creater = (FormatModelFactory) createrInfo.CreateDelegate (typeof (FormatModelFactory));
                            var names = (string[]) namesInfo.Invoke (null, null);
                            var subname = (string) (subnameInfo==null ? null : subnameInfo.Invoke (null, null));
                            FormatModel.Add (creater, names, subname);
                        }
                    }

                FormatModel.Sort();
            }
#else
            private void LoadFormats()
            {
                foreach (var type in Assembly.GetAssembly (typeof (FormatBase)).GetTypes())
                {
                    if (type.IsClass && type.Name.EndsWith ("Format"))
                    {
                        MethodInfo createrInfo = null, namesInfo = null, subnameInfo = null;
                        var modelType = type.GetNestedType ("Model");
                        foreach (var meth in type.GetMethods (BindingFlags.Public|BindingFlags.Static|BindingFlags.DeclaredOnly))
                        {
                            var ret = meth.ReturnType;
                            if (! meth.IsSpecialName)
                            {
                                if (meth.Name == "CreateModel")
                                {
                                    var parm = meth.GetParameters();
                                    while (ret.BaseType != typeof (System.Object))
                                        ret = ret.BaseType;
                                    if (ret == typeof (FormatBase.ModelBase) && parm.Length==3
                                            && parm[0].ParameterType == typeof (Stream)
                                            && parm[1].ParameterType == typeof (System.Byte[])
                                            && parm[2].ParameterType == typeof (String))
                                        createrInfo = meth;
                                }
                            }
                            else if (meth.Name=="get_Names" && ret == typeof (System.String[]))
                                namesInfo = meth;
                            else if (meth.IsSpecialName && meth.Name=="get_Subname" && ret == typeof (System.String))
                                subnameInfo = meth;
                        }

                        if (namesInfo != null && createrInfo != null)
                        {
                            var names = (string[]) namesInfo.Invoke (null, null);
                            var subname = (string) subnameInfo?.Invoke (null, null);
                            var creater = (FormatModelFactory) Delegate.CreateDelegate (typeof (FormatModelFactory), createrInfo);
                            FormatModel.Add (creater, names, subname);
                        }
                    }
                }

                FormatModel.Sort();
            }
#endif

            public void ResetTotals()
            {
                Bind.TotalFiles = 0;
                Bind.TotalRepairable = 0;
                Bind.TotalErrors = 0;
                Bind.TotalWarnings = 0;
                Bind.TotalSignable = 0;

                FormatModel.ResetTotals();
            }


            public void SetCurrentFile (string baseName, Granularity stickyScope)
            {
                Bind.CurrentFile = baseName;
                Bind.OnFileVisit (Bind.CurrentDirectory, baseName);

                if (stickyScope >= Bind.Scope)
                    Bind.OnMessageSend (null);
            }


            public void SetCurrentFile (string baseName)
            {
                Bind.CurrentFile = baseName;
                Bind.OnFileVisit (Bind.CurrentDirectory, baseName);
            }


            public void SetCurrentFile (string directoryName, string fileName)
            {
                Bind.CurrentFile = fileName;
                Bind.CurrentDirectory = directoryName;
                Bind.OnFileVisit (directoryName, fileName);
            }


            public void SetCurrentDirectory (string directoryName)
            {
                Bind.CurrentFile = null;
                Bind.CurrentDirectory = directoryName;
                Bind.OnFileVisit (directoryName, null);
            }


            public virtual void ReportLine (string message, Severity severity = Severity.Error, bool logErrToFile = false)
            {
                if (Bind.CurrentFile != null)
                    if (severity >= Severity.Error)
                        ++Bind.TotalErrors;
                    else if (severity == Severity.Warning)
                        ++Bind.TotalWarnings;

                if ((int) severity >= (int) Bind.Scope)
                    Bind.OnMessageSend (message, severity);
            }


            public virtual void ReportFormat (FormatBase fb, bool logErrorsToFile = false)
            {
                Granularity scope = Bind.Scope;
                if (scope <= Granularity.Long)
                {
                    IList<string> report = fb.GetDetailsHeader (scope);
                    fb.GetDetailsBody (report, scope);

                    Bind.OnMessageSend (String.Empty, Severity.NoIssue);

                    foreach (var lx in report)
                        Bind.OnMessageSend (lx);
                }
                else if (scope > Granularity.Terse)
                    if (Bind.Response == Interaction.PromptToRepair && fb.Issues.RepairableCount > 0)
                        scope = Granularity.Terse;

                bool hasWarn = false, hasErr = false;
                int shownIssuesCount = 0;

                foreach (var item in fb.Issues.Items)
                {
                    Severity severity = item.Level;
                    if (severity == Severity.Warning)
                    {
                        if (! hasWarn)
                        {
                            hasWarn = true;
                            ++Bind.TotalWarnings;
                        }
                    }
                    else if (severity >= Severity.Error)
                    {
                        if (! hasErr)
                        {
                            hasErr = true;
                            ++Bind.TotalErrors;
                        }
                    }

                    if (item.IsReportable (scope))
                    {
                        if (shownIssuesCount == 0)
                            if (scope <= Granularity.Long)
                                if (scope == Granularity.Long)
                                    Bind.OnMessageSend ("Diagnostics:", Severity.NoIssue);
                                else
                                { Bind.OnMessageSend (String.Empty); Bind.OnMessageSend ("Diagnostics:"); }
                        ++shownIssuesCount;

                        Bind.OnMessageSend (item.Message, severity, item.IsRepairable? Likeliness.Probable : Likeliness.None);
                    }
                }
            }


            private bool RepairFile (FormatBase.ModelBase formatModel)
            {
                bool result = false;

                if (Bind.Response == Interaction.PromptToRepair)
                {
                    for (int ix = 0; ix < formatModel.BaseBind.Issues.Items.Count; ++ix)
                    {
                        Issue issue = formatModel.BaseBind.Issues.Items[ix];
                        if (issue.IsRepairable)
                            for (;;)
                            {
                                Bind.OnMessageSend (String.Empty, Severity.NoIssue);
                                bool? isYes = Bind.QuestionAsk (issue.RepairPrompt + "? ");
                                if (isYes != true)
                                    break;

                                string errorMessage = formatModel.IssueModel.Repair (ix);
                                if (errorMessage == null)
                                {
                                    Bind.OnMessageSend ("Repair successful!", Severity.Advisory);
                                    if (formatModel.IssueModel.RepairerEquals (ix, formatModel.RepairWrongExtension))
                                        result = true;
                                    break;
                                }

                                Bind.OnMessageSend ("Repair attempt failed: " + errorMessage, Severity.Warning);
                            }
                    }

                    Bind.OnMessageSend (String.Empty, Severity.NoIssue);
                    formatModel.CloseFile();
                }

                return result;
            }
        }
    }
}
