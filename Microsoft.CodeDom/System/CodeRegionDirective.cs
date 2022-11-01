// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.CodeDom
{
    using System.Diagnostics;
    using System;
    using Microsoft.Win32;
    using System.Collections;
    using System.Runtime.InteropServices;

    [
        //  ClassInterface(ClassInterfaceType.AutoDispatch),
        ComVisible(true),
    // Serializable,
    ]
    public class CodeRegionDirective : CodeDirective
    {
        private string _regionText;
        private CodeRegionMode _regionMode;

        public CodeRegionDirective()
        {
        }

        public CodeRegionDirective(CodeRegionMode regionMode, string regionText)
        {
            this.RegionText = regionText;
            _regionMode = regionMode;
        }

        public string RegionText
        {
            get
            {
                return (_regionText == null) ? string.Empty : _regionText;
            }
            set
            {
                _regionText = value;
            }
        }

        public CodeRegionMode RegionMode
        {
            get
            {
                return _regionMode;
            }
            set
            {
                _regionMode = value;
            }
        }
    }
}
