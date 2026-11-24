using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TrajectorSphere : MonoBehaviour
{
    // public Transform centerEyeAnchor;
    [SerializeField] int y = 0;
    
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        this.transform.position = new Vector3(this.transform.position.x, y, this.transform.position.z);
    }
}
