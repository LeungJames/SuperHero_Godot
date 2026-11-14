/// <summary>可被子弹击中的关卡物体（爆炸桶等），由武器在结算后回调。</summary>
public interface IBulletReceiver
{
	void OnBulletHit(in HitscanHit hit);
}
