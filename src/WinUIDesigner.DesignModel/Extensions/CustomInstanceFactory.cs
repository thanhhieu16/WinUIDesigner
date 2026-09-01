// Copyright (c) 2019 AlphaSierraPapa for the SharpDevelop Team
//
// Permission is hereby granted, free of charge, to any person obtaining a copy of this
// software and associated documentation files (the "Software"), to deal in the Software
// without restriction, including without limitation the rights to use, copy, modify, merge,
// publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons
// to whom the Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all copies or
// substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
// PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE
// FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
// OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

using System;

namespace WinUIDesigner.DesignModel.Extensions
{
    [ExtensionServer(typeof(NeverApplyExtensionsExtensionServer))]
    public class CustomInstanceFactory : Extension
    {
        public static readonly CustomInstanceFactory DefaultInstanceFactory = new CustomInstanceFactory();

        protected CustomInstanceFactory() { }

        /// <summary>
        /// Creates an instance of the specified type, passing the specified arguments to its constructor.
        /// Unlike the WPF reference, this does not mark the instance as design-time via
        /// DesignerProperties.SetIsInDesignMode - WinUI has no per-instance equivalent
        /// (Windows.ApplicationModel.DesignMode.DesignModeEnabled is process-wide and read-only).
        /// </summary>
        public virtual object CreateInstance(Type type, params object[] arguments)
        {
            return Activator.CreateInstance(type, arguments);
        }
    }
}
