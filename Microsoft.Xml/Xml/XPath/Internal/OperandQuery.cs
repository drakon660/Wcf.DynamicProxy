// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace MS.Internal.Xml.XPath
{
    using System;
    using Microsoft.Xml;
    using Microsoft.Xml.XPath;
    using System.Diagnostics;
    using System.Globalization;
    using System.Collections;

    internal sealed class OperandQuery : ValueQuery
    {
        internal object val;

        public OperandQuery(object val)
        {
            this.val = val;
        }

        public override object Evaluate(XPathNodeIterator nodeIterator)
        {
            return val;
        }
        public override XPathResultType StaticType { get { return GetXPathType(val); } }
        public override XPathNodeIterator Clone() { return this; }

        public override void PrintQuery(XmlWriter w)
        {
            w.WriteStartElement(this.GetType().Name);
            w.WriteAttributeString("value", Convert.ToString(val, CultureInfo.InvariantCulture));
            w.WriteEndElement();
        }
    }
}
