using LLVMSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Text;

namespace GPUCollection
{
    /// <summary>
    /// Boilerplate code from https://blogs.msdn.microsoft.com/mattwar/2007/07/31/linq-building-an-iqueryable-provider-part-ii/
    /// </summary>
    class LLVMBitCodeVisitor : ExpressionVisitor
    {
        private const string ModuleNameString = "GPUCollection";
        private const string BitCodeFilename = "test.bc";

        LLVMModuleRef module;
        LLVMBuilderRef mainBuilder;
        LLVMValueRef mainFunc;
        int functionNumber;
        Stack<LLVMValueRef> mainFunctionRefStack;

        internal LLVMBitCodeVisitor()
        {
        }

        internal string WalkTree(Expression expression)
        {
            InitializeLLVMWriting();
            this.Visit(expression);
            string bitCodeFilePath = FinalizeLLVMWriting();
            return bitCodeFilePath;
        }

        /// <summary>
        /// Modifiying https://github.com/paulsmith/getting-started-llvm-c-api/blob/master/sum.c
        /// </summary>
        private void InitializeLLVMWriting()
        {
            functionNumber = 0;
            module = LLVM.ModuleCreateWithName(ModuleNameString);
            LLVMTypeRef mainRetType = LLVM.FunctionType(LLVM.Int32Type(), new LLVMTypeRef[] { LLVM.Int32Type() }, false);
            mainFunc = LLVM.AddFunction(module, "CSMain", mainRetType);

            mainBuilder = LLVM.CreateBuilder();
            LLVMBasicBlockRef mainBlock = LLVM.AppendBasicBlock(mainFunc, "EntryBlock");
            LLVM.PositionBuilderAtEnd(mainBuilder, mainBlock);

            // Initialize the first statement as a constant
            mainFunctionRefStack = new Stack<LLVMValueRef>();
        }

        /// <summary>
        /// Modifiying https://github.com/paulsmith/getting-started-llvm-c-api/blob/master/sum.c
        /// </summary>
        private string FinalizeLLVMWriting()
        {
            LLVM.BuildRet(mainBuilder, mainFunctionRefStack.Pop());

            string outErrorMessage;
            LLVM.VerifyModule(module, LLVMVerifierFailureAction.LLVMAbortProcessAction, out outErrorMessage);
            string fullFilePath = Path.Combine(System.IO.Directory.GetCurrentDirectory(), BitCodeFilename);
            if (LLVM.WriteBitcodeToFile(module, fullFilePath) != 0)
            {
                throw new IOException($"Unable to write to {fullFilePath}");
            }

            return fullFilePath;
        }

        private static Expression StripQuotes(Expression e)
        {
            while (e.NodeType == ExpressionType.Quote)
            {
                e = ((UnaryExpression)e).Operand;
            }
            return e;
        }

        protected override Expression VisitMethodCall(MethodCallExpression m)
        {
            if (m.Method.DeclaringType == typeof(Queryable) && m.Method.Name == "Where")
            {
                this.Visit(m.Arguments[0]);
                LambdaExpression lambda = (LambdaExpression)StripQuotes(m.Arguments[1]);
                this.Visit(lambda.Body);
                return m;
            }
            else if (m.Method.DeclaringType == typeof(Queryable) && m.Method.Name == "Select")
            {
                LambdaExpression lambda = (LambdaExpression)StripQuotes(m.Arguments[1]);
                this.Visit(lambda.Body);
                return m;
            }

            throw new NotSupportedException(string.Format("The method '{0}' is not supported", m.Method.Name));
        }

        protected override Expression VisitUnary(UnaryExpression u)
        {
            switch (u.NodeType)
            {
                case ExpressionType.Not:
                    // TODO: something meaningful
                    this.Visit(u.Operand);
                    break;
                default:
                    throw new NotSupportedException(string.Format("The unary operator '{0}' is not supported", u.NodeType));
            }

            return u;
        }
        protected override Expression VisitParameter(ParameterExpression node)
        {
            mainFunctionRefStack.Push(LLVM.GetParam(mainFunc, 0));
            return node;
        }

        protected override Expression VisitBinary(BinaryExpression b)
        {
            this.Visit(b.Left);
            this.Visit(b.Right);

            string name = "op" + functionNumber + "Result";
            var second = mainFunctionRefStack.Pop();
            var first = mainFunctionRefStack.Pop();
            switch (b.NodeType)
            {
                case ExpressionType.Add:
                    if (b.Type == typeof(float) || b.Type == typeof(double))
                        mainFunctionRefStack.Push(LLVM.BuildFAdd(mainBuilder, first, second, name));
                    else
                        mainFunctionRefStack.Push(LLVM.BuildAdd(mainBuilder, first, second, name));
                    break;
                case ExpressionType.Subtract:
                    if (b.Type == typeof(float) || b.Type == typeof(double))
                        mainFunctionRefStack.Push(LLVM.BuildFSub(mainBuilder, first, second, name));
                    else
                        mainFunctionRefStack.Push(LLVM.BuildSub(mainBuilder, first, second, name));
                    break;
                case ExpressionType.Multiply:
                    if (b.Type == typeof(float) || b.Type == typeof(double))
                        mainFunctionRefStack.Push(LLVM.BuildFMul(mainBuilder, first, second, name));
                    else
                        mainFunctionRefStack.Push(LLVM.BuildMul(mainBuilder, first, second, name));
                    break;
                case ExpressionType.Divide:
                    if (b.Type == typeof(float) || b.Type == typeof(double))
                        mainFunctionRefStack.Push(LLVM.BuildFDiv(mainBuilder, first, second, name));
                    else if (IsUnsignedType(b.Type))
                        mainFunctionRefStack.Push(LLVM.BuildUDiv(mainBuilder, first, second, name));
                    else
                        mainFunctionRefStack.Push(LLVM.BuildSDiv(mainBuilder, first, second, name));
                    break;
                default:
                    throw new NotSupportedException(string.Format("The binary operator '{0}' is not supported", b.NodeType));
            }
            functionNumber++;

            return b;
        }

        private static bool IsUnsignedType(Type type)
        {
            return type == typeof(byte)
                || type == typeof(char)
                || type == typeof(uint)
                || type == typeof(ulong)
                || type == typeof(ushort);
        }

        protected override Expression VisitConstant(ConstantExpression c)
        {
            IQueryable q = c.Value as IQueryable;
            if (q != null)
            {
                // TODO: something meaningful
            }
            else if (c.Value == null)
            {
                // TODO: something meaningful
            }
            else
            {
                switch (System.Type.GetTypeCode(c.Value.GetType()))
                {
                    case TypeCode.Int32:
                        mainFunctionRefStack.Push(LLVM.ConstInt(LLVM.Int32Type(), Convert.ToUInt64((object)c.Value), new LLVMBool(0)));
                        break;
                    case TypeCode.Double:
                        // Figure out how to create a const double
                        // mainFunctionRefStack.Push(LLVM.ConstInt(LLVM.Int32Type(), Convert.ToUInt64((object)c.Value), new LLVMBool(0)));
                        break;
                    case TypeCode.Object:
                        throw new NotSupportedException(string.Format("The constant for '{0}' is not supported", c.Value));
                    default:
                        // TODO: something meaningful
                        break;
                }
            }

            return c;
        }

        protected override Expression VisitMember(MemberExpression m)
        {
            if (m.Expression != null && m.Expression.NodeType == ExpressionType.Parameter)
            {
                // TODO: something meaningful
                return m;
            }
            throw new NotSupportedException(string.Format("The member '{0}' is not supported", m.Member.Name));
        }
    }
}
