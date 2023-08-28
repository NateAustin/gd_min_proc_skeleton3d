using Godot;

namespace ProcSkeleton3D;

public partial class Skel3DExample : Node3D
{
	
	private Skeleton3D skeleton3d;
	private float time;
	
	
	public override void _Ready()
	{
		skeleton3d = new Skeleton3D();
		skeleton3d.Name = "skeleton3d";
		var mesh = new MeshInstance3D();
		AddChild(skeleton3d);
		AddChild(mesh);


		int boneCount = 2;
		{
			int boneIdx = 0;
			//create first bone:
			{
				skeleton3d.AddBone("foot");
				skeleton3d.SetBoneRest(boneIdx, Transform3D.Identity);
			}

			boneIdx++;
			//create 2nd bone:
			{
				skeleton3d.AddBone("head");
				skeleton3d.SetBoneParent(boneIdx, 0);
				var t = Transform3D.Identity;
				t.Origin = new Vector3(0, 1, 0);
				skeleton3d.SetBoneRest(boneIdx, t);
			}
		}

		//reset all bones to their rest poses:
		skeleton3d.ResetBonePoses();

		//assign skeleton to mesh now:
		var nodePath = mesh.GetPathTo(skeleton3d);
		mesh.Skeleton = nodePath;
		
		//generate a simple mesh (quad)
		{
			var st = new SurfaceTool();
			
			st.Begin(Mesh.PrimitiveType.Triangles);
			
			//0 - bottom left corner
			st.SetUV(new Vector2(0, 1));
			st.SetBones(new []{0, 0, 0, 0});
			st.SetWeights(new float[]{1, 0, 0, 0});
			st.AddVertex(new Vector3(-0.5f, 0, 0));
			//1 - top left corner
			st.SetUV(new Vector2(0, 0));
			st.SetBones(new []{1, 0, 0, 0});
			st.SetWeights(new float[]{1, 0, 0, 0});
			st.AddVertex(new Vector3(-0.5f, 1, 0));
			//2 - top right corner
			st.SetUV(new Vector2(1, 0));
			st.SetBones(new []{1, 0, 0, 0});
			st.SetWeights(new float[]{1, 0, 0, 0});
			st.AddVertex(new Vector3(0.5f, 1, 0));
			//3 - bottom right corner
			st.SetUV(new Vector2(1, 1));
			st.SetBones(new []{0, 0, 0, 0});
			st.SetWeights(new float[]{1, 0, 0, 0});
			st.AddVertex(new Vector3(0.5f, 0, 0));
			
			
			//draw a quad with two tris
			st.AddIndex(0);
			st.AddIndex(1);
			st.AddIndex(2);
			st.AddIndex(0);
			st.AddIndex(2);
			st.AddIndex(3);
			
			st.GenerateNormals();
			var arrayMesh = new ArrayMesh();
			st.Commit(arrayMesh);
			st.Clear();
			
			var mat = GD.Load<Material>("res://mat/figure.tres");
			arrayMesh.SurfaceSetMaterial(0, mat);
			mesh.Mesh = arrayMesh;
		}
		
		//finally, create the skin.
		//(I'm not sure why this step seems to be optional here but required in Character2D3D.)
		mesh.Skin = skeleton3d.CreateSkinFromRestTransforms();
	}


	public override void _Process(double delta)
	{

		time += (float)delta;

		//animate the bones!
		var t = Transform3D.Identity;
		t.Origin = new Vector3(Mathf.Cos(time), 1 + Mathf.Sin(time), 0f);
		skeleton3d.SetBoneGlobalPoseOverride(1, t, 1, true);

	}
}