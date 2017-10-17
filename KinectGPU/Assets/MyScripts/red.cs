using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class red : MonoBehaviour {
    public Material mat;
    public ComputeShader shader;
	// Use this for initialization
	void Start () {
        RunShader();
	}
	void RunShader()
    {
        // 新建rendertexture
        RenderTexture tex = new RenderTexture(256, 256, 24);
        // 开启随机写入
        tex.enableRandomWrite = true;
        // 创建
        tex.Create();
        // 赋予材质
        mat.mainTexture = tex;

        // 找到kernel
        int kernel = shader.FindKernel("CSMain");
        // 设置贴图
        shader.SetTexture(kernel, "Result", tex);
        // 运行shader，线程组如何划分
        shader.Dispatch(kernel, 256 / 8, 256 / 8, 1);
    }
	// Update is called once per frame
	void Update () {
		
	}
}
