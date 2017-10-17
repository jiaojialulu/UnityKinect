using System.Collections;
using System.Collections.Generic;
using UnityEngine;

struct PBuffer
{
    public float life;
    public Vector3 pos;
    public Vector3 scale;
    public Vector3 eulerAngle;
};

public class bufferInComputeShader : MonoBehaviour {

    public ComputeShader shader;
    public GameObject prefab;
    private List<GameObject> pool = new List<GameObject>();
    int count = 16;
    private ComputeBuffer buffer;

	// Use this for initialization
	void Start () {
		for(int i = 0;i<count;i++)
        {
            GameObject obj = Instantiate(prefab) as GameObject;
            pool.Add(obj);
        }
        CreateBuffer();
	}

    void CreateBuffer()
    {
        // 第二个参数为结构体长度
        buffer = new ComputeBuffer(count, 40);
        PBuffer[] values = new PBuffer[count];
        for(int i = 0;i<count;i++)
        {
            PBuffer m = new PBuffer();
            InitStruct(ref m);
            values[i] = m;
        }
        buffer.SetData(values);
    }

    void InitStruct(ref PBuffer m)
    {
        m.life = Random.Range(1f, 3f);
        m.pos = Random.insideUnitSphere * 5f;
        m.scale = Vector3.one * Random.Range(0.3f, 1f);
        m.eulerAngle = new Vector3(0, Random.Range(0f, 180f), 0);
    }
	
	// Update is called once per frame
	void Update () {
        // 运行shader
        Dispatch();

        // 根据Shader返回的buffer数据更新物体信息
        PBuffer[] values = new PBuffer[count];
        buffer.GetData(values);
        bool reborn = false;
        for (int i = 0; i < count; i++) {
            if (values [i].life < 0) {
                InitStruct (ref values [i]);
                reborn = true;
            } else {
                pool [i].transform.position = values [i].pos;
                pool [i].transform.localScale = values [i].scale;
                pool [i].transform.eulerAngles = values [i].eulerAngle;
                //pool [i].GetComponent<</span>MeshRenderer>().material.SetColor ("_TintColor", new Color(1,1,1,values [i].life));
            }
        }
        if(reborn)
            buffer.SetData(values);
	}

    void Dispatch()
    {
        shader.SetFloat("deltaTime", Time.deltaTime);
        int kid = shader.FindKernel("CSMain");
        shader.SetBuffer(kid, "buffer", buffer);
        shader.Dispatch(kid, 2, 2, 1);
    }

    void ReleaseBuffer()
    {
        buffer.Release();
    }
    private void OnDisable()
    {
        ReleaseBuffer();
    }
}
