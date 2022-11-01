// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.CodeDom
{
    using System;
    using System.Collections;
    using System.Runtime.InteropServices;


    /// <devdoc>
    ///     <para>
    ///       A collection that stores <see cref='Microsoft.CodeDom.CodeAttributeDeclaration'/> objects.
    ///    </para>
    /// </devdoc>
    [
        //  ClassInterface(ClassInterfaceType.AutoDispatch),
        ComVisible(true),
    // Serializable,
    ]
    public class CodeAttributeDeclarationCollection : CollectionBase
    {
        /// <devdoc>
        ///     <para>
        ///       Initializes a new instance of <see cref='Microsoft.CodeDom.CodeAttributeDeclarationCollection'/>.
        ///    </para>
        /// </devdoc>
        public CodeAttributeDeclarationCollection()
        {
        }

        /// <devdoc>
        ///     <para>
        ///       Initializes a new instance of <see cref='Microsoft.CodeDom.CodeAttributeDeclarationCollection'/> based on another <see cref='Microsoft.CodeDom.CodeAttributeDeclarationCollection'/>.
        ///    </para>
        /// </devdoc>
        public CodeAttributeDeclarationCollection(CodeAttributeDeclarationCollection value)
        {
            this.AddRange(value);
        }

        /// <devdoc>
        ///     <para>
        ///       Initializes a new instance of <see cref='Microsoft.CodeDom.CodeAttributeDeclarationCollection'/> containing any array of <see cref='Microsoft.CodeDom.CodeAttributeDeclaration'/> objects.
        ///    </para>
        /// </devdoc>
        public CodeAttributeDeclarationCollection(CodeAttributeDeclaration[] value)
        {
            this.AddRange(value);
        }

        /// <devdoc>
        /// <para>Represents the entry at the specified index of the <see cref='Microsoft.CodeDom.CodeAttributeDeclaration'/>.</para>
        /// </devdoc>
        public CodeAttributeDeclaration this[int index]
        {
            get
            {
                return ((CodeAttributeDeclaration)(List[index]));
            }
            set
            {
                List[index] = value;
            }
        }

        /// <devdoc>
        ///    <para>Adds a <see cref='Microsoft.CodeDom.CodeAttributeDeclaration'/> with the specified value to the 
        ///    <see cref='Microsoft.CodeDom.CodeAttributeDeclarationCollection'/> .</para>
        /// </devdoc>
        public int Add(CodeAttributeDeclaration value)
        {
            return List.Add(value);
        }

        /// <devdoc>
        /// <para>Copies the elements of an array to the end of the <see cref='Microsoft.CodeDom.CodeAttributeDeclarationCollection'/>.</para>
        /// </devdoc>
        public void AddRange(CodeAttributeDeclaration[] value)
        {
            if (value == null)
            {
                throw new ArgumentNullException("value");
            }
            for (int i = 0; ((i) < (value.Length)); i = ((i) + (1)))
            {
                this.Add(value[i]);
            }
        }

        /// <devdoc>
        ///     <para>
        ///       Adds the contents of another <see cref='Microsoft.CodeDom.CodeAttributeDeclarationCollection'/> to the end of the collection.
        ///    </para>
        /// </devdoc>
        public void AddRange(CodeAttributeDeclarationCollection value)
        {
            if (value == null)
            {
                throw new ArgumentNullException("value");
            }
            int currentCount = value.Count;
            for (int i = 0; i < currentCount; i = ((i) + (1)))
            {
                this.Add(value[i]);
            }
        }

        /// <devdoc>
        /// <para>Gets a value indicating whether the 
        ///    <see cref='Microsoft.CodeDom.CodeAttributeDeclarationCollection'/> contains the specified <see cref='Microsoft.CodeDom.CodeAttributeDeclaration'/>.</para>
        /// </devdoc>
        public bool Contains(CodeAttributeDeclaration value)
        {
            return List.Contains(value);
        }

        /// <devdoc>
        /// <para>Copies the <see cref='Microsoft.CodeDom.CodeAttributeDeclarationCollection'/> values to a one-dimensional <see cref='System.Array'/> instance at the 
        ///    specified index.</para>
        /// </devdoc>
        public void CopyTo(CodeAttributeDeclaration[] array, int index)
        {
            List.CopyTo(array, index);
        }

        /// <devdoc>
        ///    <para>Returns the index of a <see cref='Microsoft.CodeDom.CodeAttributeDeclaration'/> in 
        ///       the <see cref='Microsoft.CodeDom.CodeAttributeDeclarationCollection'/> .</para>
        /// </devdoc>
        public int IndexOf(CodeAttributeDeclaration value)
        {
            return List.IndexOf(value);
        }

        /// <devdoc>
        /// <para>Inserts a <see cref='Microsoft.CodeDom.CodeAttributeDeclaration'/> into the <see cref='Microsoft.CodeDom.CodeAttributeDeclarationCollection'/> at the specified index.</para>
        /// </devdoc>
        public void Insert(int index, CodeAttributeDeclaration value)
        {
            List.Insert(index, value);
        }

        /// <devdoc>
        ///    <para> Removes a specific <see cref='Microsoft.CodeDom.CodeAttributeDeclaration'/> from the 
        ///    <see cref='Microsoft.CodeDom.CodeAttributeDeclarationCollection'/> .</para>
        /// </devdoc>
        public void Remove(CodeAttributeDeclaration value)
        {
            List.Remove(value);
        }
    }
}
