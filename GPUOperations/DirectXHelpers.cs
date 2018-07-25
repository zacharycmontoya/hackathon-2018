using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

using SharpDX.Direct3D;
using SharpDX.Direct3D12;
using ShaderBytecode = SharpDX.Direct3D12.ShaderBytecode;
using ShaderCompilation = SharpDX.D3DCompiler.ShaderBytecode;

namespace GPUOperations
{
    public unsafe class DirectXHelpers
    {

        public static Resource CreateBuffer(Device device, long size, HeapType heapType = HeapType.Default, ResourceFlags resourceFlags = ResourceFlags.None, ResourceStates resourceStates = ResourceStates.Common)
        {
            var heapProperties = new HeapProperties(heapType);
            return device.CreateCommittedResource(heapProperties, HeapFlags.None, ResourceDescription.Buffer(size, resourceFlags), resourceStates);
        }

        public static RootSignature CreateRootSignature(Device device, RootParameter[] rootParameters)
        {
            var rootSignatureDesc = new RootSignatureDescription(RootSignatureFlags.AllowInputAssemblerInputLayout, rootParameters);
            return device.CreateRootSignature(rootSignatureDesc.Serialize());
        }

        public static PipelineState CreateComputePipelineState(Device device, RootSignature rootSignature, byte[] computeShader)
        {
            var computePipelineStateDesc = new ComputePipelineStateDescription()
            {
                RootSignature = rootSignature,
                ComputeShader = new ShaderBytecode(computeShader),
                Flags = PipelineStateFlags.None
            };
            return device.CreateComputePipelineState(computePipelineStateDesc);
        }

    }
}
