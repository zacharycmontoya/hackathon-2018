using LLVMSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;

namespace GPUCollection
{
    /// <summary>
    /// Boilerplate code from https://blogs.msdn.microsoft.com/mattwar/2007/07/31/linq-building-an-iqueryable-provider-part-ii/
    /// </summary>
    class QueryTranslator<T> : ExpressionVisitor
    {
        private const string ModuleNameString = "GPUCollection";

        IEnumerable<T> data;
        StringBuilder sb;
        LLVMValueRef lastLLVMFunctionCalledFromMain;
        LLVMModuleRef module;
        LLVMBuilderRef mainBuilder;
        int functionNumber;

        internal QueryTranslator(IEnumerable<T> data)
        {
            this.data = data;
        }

        internal string Translate(Expression expression)
        {
            this.sb = new StringBuilder();
            InitializeLLVMWriting();
            this.Visit(expression);
            FinalizeLLVMWriting();
            return this.sb.ToString();
        }

        /// <summary>
        /// Modifiying https://github.com/paulsmith/getting-started-llvm-c-api/blob/master/sum.c
        /// </summary>
        private void InitializeLLVMWriting()
        {
            functionNumber = 0;
            module = LLVM.ModuleCreateWithName(ModuleNameString);
            LLVMTypeRef mainRetType = LLVM.FunctionType(LLVM.Int32Type(), new LLVMTypeRef[0], false);
            LLVMValueRef mainFunc = LLVM.AddFunction(module, "main", mainRetType);
            mainBuilder = LLVM.CreateBuilder();
            LLVMBasicBlockRef mainBlock = LLVM.AppendBasicBlock(mainFunc, "MainEntry");
            LLVM.PositionBuilderAtEnd(mainBuilder, mainBlock);
        }

        /// <summary>
        /// Modifiying https://github.com/paulsmith/getting-started-llvm-c-api/blob/master/sum.c
        /// </summary>
        private void FinalizeLLVMWriting()
        {
            LLVM.BuildRet(mainBuilder, lastLLVMFunctionCalledFromMain);

            string outErrorMessage;
            LLVM.VerifyModule(module, LLVMVerifierFailureAction.LLVMAbortProcessAction, out outErrorMessage);
            LLVM.WriteBitcodeToFile(module, "test.bc");
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
                sb.Append("SELECT * FROM (");
                this.Visit(m.Arguments[0]);
                sb.Append(") AS T WHERE ");
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
                    sb.Append(" NOT ");
                    this.Visit(u.Operand);
                    break;
                default:
                    throw new NotSupportedException(string.Format("The unary operator '{0}' is not supported", u.NodeType));
            }

            return u;
        }

        protected override Expression VisitBinary(BinaryExpression b)
        {
            sb.Append("(");
            this.Visit(b.Left);

            Type retType = b.Type;
            LLVMTypeRef llvmRetType;

            if (retType == typeof(Int32))
            {
                llvmRetType = LLVM.Int32Type();
            }
            else if (retType == typeof(Double))
            {
                llvmRetType = LLVM.DoubleType();
            }
            else if (retType == typeof(float))
            {
                llvmRetType = LLVM.FloatType();
            }
            else
            {
                throw new NotSupportedException(string.Format("The binary operator does not support operands of type '{0}' is not supported", retType.ToString()));
            }

            LLVMTypeRef[] opParamTypes = new LLVMTypeRef[] { llvmRetType, llvmRetType };
            LLVMTypeRef opRetType = LLVM.FunctionType(llvmRetType, opParamTypes, false);
            LLVMValueRef opFunc = LLVM.AddFunction(module, "op" + functionNumber, opRetType);

            LLVMBasicBlockRef entry = LLVM.AppendBasicBlock(opFunc, "entry");

            LLVMBuilderRef opBuilder = LLVM.CreateBuilder();
            LLVM.PositionBuilderAtEnd(opBuilder, entry);
            LLVMValueRef tmp = GenerateOp(b.NodeType, opBuilder, opFunc);
            LLVM.BuildRet(opBuilder, tmp);

            T[] dataInArray = data.ToArray();
            if (retType == typeof(Int32))
                lastLLVMFunctionCalledFromMain = LLVM.BuildCall(mainBuilder, opFunc, new LLVMValueRef[] { LLVM.ConstInt(llvmRetType, Convert.ToUInt64((object)dataInArray[0]), new LLVMBool(0)), LLVM.ConstInt(llvmRetType, Convert.ToUInt64((object)dataInArray[1]), new LLVMBool(0)) }, "functioncall");
            else
                throw new NotSupportedException(string.Format("The binary operator does not yet support return type '{0}'", retType.Name));

            functionNumber++;

            this.Visit(b.Right);
            sb.Append(")");
            return b;
        }

        private LLVMValueRef GenerateOp(ExpressionType expressionType, LLVMBuilderRef opBuilder, LLVMValueRef opFunc)
        {
            string name = "op" + functionNumber + "Result";
            switch (expressionType)
            {
                case ExpressionType.Add:
                    return LLVM.BuildAdd(opBuilder, LLVM.GetParam(opFunc, 0), LLVM.GetParam(opFunc, 1), name);
                case ExpressionType.Subtract:
                    return LLVM.BuildSub(opBuilder, LLVM.GetParam(opFunc, 0), LLVM.GetParam(opFunc, 1), name);
                default:
                    throw new NotSupportedException(string.Format("The binary operator '{0}' is not supported", expressionType));
            }

        }

        protected override Expression VisitConstant(ConstantExpression c)
        {
            IQueryable q = c.Value as IQueryable;
            if (q != null)
            {
                // assume constant nodes w/ IQueryables are table references
                sb.Append("SELECT * FROM ");
                sb.Append(q.ElementType.Name);
            }
            else if (c.Value == null)
            {
                sb.Append("NULL");
            }
            else
            {
                switch (System.Type.GetTypeCode(c.Value.GetType()))
                {
                    case TypeCode.Boolean:
                        sb.Append(((bool)c.Value) ? 1 : 0);
                        break;
                    case TypeCode.String:
                        sb.Append("'");
                        sb.Append(c.Value);
                        sb.Append("'");
                        break;
                    case TypeCode.Object:
                        throw new NotSupportedException(string.Format("The constant for '{0}' is not supported", c.Value));
                    default:
                        sb.Append(c.Value);
                        break;
                }
            }

            return c;
        }

        protected override Expression VisitMember(MemberExpression m)
        {
            if (m.Expression != null && m.Expression.NodeType == ExpressionType.Parameter)
            {
                sb.Append(m.Member.Name);
                return m;
            }
            throw new NotSupportedException(string.Format("The member '{0}' is not supported", m.Member.Name));
        }
    }
}
