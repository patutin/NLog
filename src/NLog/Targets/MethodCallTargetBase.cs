// 
// Copyright (c) 2004-2017 Jaroslaw Kowalski <jaak@jkowalski.net>, Kim Christensen, Julian Verdurmen
// 
// All rights reserved.
// 
// Redistribution and use in source and binary forms, with or without 
// modification, are permitted provided that the following conditions 
// are met:
// 
// * Redistributions of source code must retain the above copyright notice, 
//   this list of conditions and the following disclaimer. 
// 
// * Redistributions in binary form must reproduce the above copyright notice,
//   this list of conditions and the following disclaimer in the documentation
//   and/or other materials provided with the distribution. 
// 
// * Neither the name of Jaroslaw Kowalski nor the names of its 
//   contributors may be used to endorse or promote products derived from this
//   software without specific prior written permission. 
// 
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
// AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE 
// IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE 
// ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE 
// LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR 
// CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF
// SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS 
// INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN 
// CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) 
// ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF 
// THE POSSIBILITY OF SUCH DAMAGE.
// 

namespace NLog.Targets
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;
    using NLog.Common;
    using NLog.Config;
    using NLog.Internal;

    /// <summary>
    /// The base class for all targets which call methods (local or remote). 
    /// Manages parameters and type coercion.
    /// </summary>
    public abstract class MethodCallTargetBase : Target
    {
        private const int MaxGroupRenderSingleBufferLength = 128 * 1024;

        /// <summary>
        /// Initializes a new instance of the <see cref="MethodCallTargetBase" /> class.
        /// </summary>
        protected MethodCallTargetBase()
        {
            this.Parameters = new List<MethodCallParameter>();
        }

        /// <summary>
        /// Gets the array of parameters to be passed.
        /// </summary>
        /// <docgen category='Parameter Options' order='10' />
        [ArrayParameter(typeof(MethodCallParameter), "parameter")]
        public IList<MethodCallParameter> Parameters { get; private set; }

        /// <summary>
        /// Prepares an array of parameters to be passed based on the logging event and calls DoInvoke().
        /// </summary>
        /// <param name="logEvent">The logging event.</param>
        protected override void Write(AsyncLogEventInfo logEvent)
        {
            object[] parameters = ConvetToParameterArray(logEvent.LogEvent, false);
            this.DoInvoke(parameters, logEvent);
        }

        internal object[] ConvetToParameterArray(LogEventInfo logEvent, bool ignoreGroupParameters)
        {
            object[] parameters = new object[this.Parameters.Count];

            for (int i = 0; i < parameters.Length; ++i)
            {
                var param = this.Parameters[i];
                if (!param.EnableGroupLayout)
                {
                    var parameterValue = base.RenderLogEvent(param.Layout, logEvent);
                    parameters[i] = Convert.ChangeType(parameterValue, param.ParameterType, CultureInfo.InvariantCulture);
                }
                else if (!ignoreGroupParameters)
                {
                    using (var targetBuilder = this.OptimizeBufferReuse ? this.ReusableLayoutBuilder.Allocate() : this.ReusableLayoutBuilder.None)
                    {
                        StringBuilder sb = targetBuilder.Result ?? new StringBuilder();
                        if (param.GroupHeaderLayout != null)
                            param.GroupHeaderLayout.RenderAppendBuilder(logEvent, sb);
                        if (param.Layout != null)
                            param.Layout.RenderAppendBuilder(logEvent, sb);
                        if (param.GroupFooterLayout != null)
                            param.GroupFooterLayout.RenderAppendBuilder(logEvent, sb);
                        parameters[i] = sb.ToString();
                    }
                }
            }

            return parameters;
        }

        internal string ConvertParameterGroupValue(IList<AsyncLogEventInfo> logEvents, MethodCallParameter param)
        {
            using (var targetBuilder = this.OptimizeBufferReuse && logEvents.Count <= 1000 ? this.ReusableLayoutBuilder.Allocate() : this.ReusableLayoutBuilder.None)
            {
                StringBuilder sb = targetBuilder.Result ?? new StringBuilder();
                if (param.GroupHeaderLayout != null)
                    param.GroupHeaderLayout.RenderAppendBuilder(logEvents[0].LogEvent, sb);
                for (int x = 0; x < logEvents.Count; ++x)
                {
                    if (x != 0 && param.GroupItemSeparatorLayout != null)
                        param.GroupItemSeparatorLayout.RenderAppendBuilder(logEvents[x].LogEvent, sb);

                    if (param.Layout != null)
                    {
                        if (sb.Length < MaxGroupRenderSingleBufferLength)
                        {
                            param.Layout.RenderAppendBuilder(logEvents[x].LogEvent, sb);
                        }
                        else
                        {
                            using (var localTarget = new AppendBuilderCreator(sb, 16))
                            {
                                param.Layout.RenderAppendBuilder(logEvents[x].LogEvent, localTarget.Builder);
                            }
                        }
                    }
                }
                if (param.GroupFooterLayout != null)
                    param.GroupFooterLayout.RenderAppendBuilder(logEvents[logEvents.Count - 1].LogEvent, sb);
                return sb.ToString();
            }
        }

        /// <summary>
        /// Calls the target DoInvoke method, and handles AsyncContinuation callback
        /// </summary>
        /// <param name="parameters">Method call parameters.</param>
        /// <param name="logEvent">The logging event.</param>
        protected virtual void DoInvoke(object[] parameters, AsyncLogEventInfo logEvent)
        {
            DoInvoke(parameters, logEvent.Continuation);
        }

        /// <summary>
        /// Calls the target DoInvoke method, and handles AsyncContinuation callback
        /// </summary>
        /// <param name="parameters">Method call parameters.</param>
        /// <param name="continuation">The continuation.</param>
        protected virtual void DoInvoke(object[] parameters, AsyncContinuation continuation)
        {
            try
            {
                this.DoInvoke(parameters);
                continuation(null);
            }
            catch (Exception ex)
            {
                if (ex.MustBeRethrown())
                {
                    throw;
                }

                continuation(ex);
            }
        }

        /// <summary>
        /// Calls the target method. Must be implemented in concrete classes.
        /// </summary>
        /// <param name="parameters">Method call parameters.</param>
        protected abstract void DoInvoke(object[] parameters);
    }
}
