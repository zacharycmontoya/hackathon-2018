using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq.Expressions;
using System.Text;

namespace GPUCollection
{
    /// <summary>
    /// Boilerplate code from https://blogs.msdn.microsoft.com/mattwar/2007/07/31/linq-building-an-iqueryable-provider-part-ii/
    /// </summary>
    public class GPUQueryProvider : BaseQueryProvider
    {
        Object arg;
        string objPath;

        public GPUQueryProvider(Object arg)
        {
            this.arg = arg;
        }

        public override string GetQueryText(Expression expression)
        {
            return this.Translate(expression);
        }

        public override object Execute(Expression expression)
        {
            objPath = "expression.bc";
            Type elementType = TypeSystem.GetElementType(expression.Type);
            this.Translate(expression, objPath); // Call translate to compile the expression to LLVM
            // LLVM IR to DXIL transition here


            // Get the results of the pipeline
            float[] result = GPUOperations.Operations.Add(objPath);
            //foreach(var resultItem in result)
                Console.WriteLine(result.Length);

            return result;
            //return new int[] { };

            /*
            return Activator.CreateInstance(
                typeof(ObjectReader<>).MakeGenericType(elementType),
                BindingFlags.Instance | BindingFlags.NonPublic, null,
                new object[] { reader },
                null);
            */
        }

        private string Translate(Expression expression, string objPath = null)
        {
            return new QueryTranslator().Translate(expression, objPath);
        }
    }
}
