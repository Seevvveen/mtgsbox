//[Icon( "Favorite", "rgb(255,255,255)", "rgb(255,255,255)" )]

public sealed class AttributesTest : Component
{
	[Property]
	public bool Toggle { get; set; }

	[Property, Validate( "SimpleMethod", "ValidateMessage", LogLevel.Warn )]
	public int MyProperty { get; set; }

	[Property]
	public float MyPropertyFloatVersion { get; set; } = 50;

	[Property, Normal]
	public Vector3 MySecondProperty { get; set; }

	//[Property]
	//public string MyThirdProperty { get; set; }

	//[Property]
	//public string MyFourthProperty { get; set; }


	private int myPropertyFullBackend;
	public int MyPropertyFull
	{
		get { return myPropertyFullBackend; ; }
		set { myPropertyFullBackend = value; }
	}


	bool SimpleMethod( int Var )
	{
		if ( Toggle ) { return true; } else { return false; }
	}

	protected override void OnEnabled()
	{
		Log.Info( MyProperty );
	}


	protected override void OnUpdate()
	{

	}
}
