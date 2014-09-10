The Microsoft.Kinect.Wpf.Controls NuGet package has added HandPointerStyles.xaml to your project.

We recommend that you link it into your app styles via something like the following inside your Application element in App.xaml:
     <Application.Resources>
         <ResourceDictionary>
             <ResourceDictionary.MergedDictionaries>
                 <ResourceDictionary Source="HandPointers\HandPointerStyles.xaml" />
             </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Application.Resources>
