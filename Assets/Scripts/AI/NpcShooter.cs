using UnityEngine;

// Shared projectile spawning for velocity-leading single shots and spread volleys.
public static class NpcShooter
{
    public static void FireLeadingShot(
        GameObject bulletPrefab, Transform firePoint, Transform target, Rigidbody targetRb,
        float bulletSpeed, int damage, float aimSpread, DamageProfile profile, string shooterName)
    {
        if (bulletPrefab == null || firePoint == null || target == null) return;

        Vector3 aimPoint = target.position;
        if (targetRb != null)
        {
            float travelTime = Vector3.Distance(firePoint.position, aimPoint) / bulletSpeed;
            aimPoint += targetRb.linearVelocity * travelTime;
        }

        Vector3 direction = aimPoint - firePoint.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.001f) return;

        direction = Quaternion.Euler(0f, Random.Range(-aimSpread, aimSpread), 0f) * direction.normalized;
        SpawnBullet(bulletPrefab, firePoint.position, direction, bulletSpeed, damage, profile, shooterName);
    }

    public static void FireSpreadVolley(
        GameObject bulletPrefab, Transform firePoint, Transform target,
        int pellets, float spreadAngle, float bulletSpeed, int damage, DamageProfile profile, string shooterName)
    {
        if (bulletPrefab == null || firePoint == null || target == null) return;

        Vector3 aim = target.position - firePoint.position;
        aim.y = 0f;
        if (aim.sqrMagnitude < 0.001f) return;
        aim.Normalize();

        float firstAngle = -spreadAngle * (pellets - 1) * 0.5f;
        for (int i = 0; i < pellets; i++)
        {
            Vector3 direction = Quaternion.Euler(0f, firstAngle + spreadAngle * i, 0f) * aim;
            SpawnBullet(bulletPrefab, firePoint.position, direction, bulletSpeed, damage, profile, shooterName);
        }
    }

    private static void SpawnBullet(
        GameObject bulletPrefab, Vector3 spawnPosition, Vector3 direction,
        float speed, int damage, DamageProfile profile, string shooterName)
    {
        GameObject bullet = Object.Instantiate(bulletPrefab, spawnPosition, Quaternion.LookRotation(direction));

        Bullet bulletScript = bullet.GetComponent<Bullet>();
        if (bulletScript != null)
        {
            bulletScript.firedByPlayer = false;
            bulletScript.damage = damage;
            bulletScript.damageProfile = profile;
            bulletScript.shooterName = shooterName;
        }

        Rigidbody bulletRb = bullet.GetComponent<Rigidbody>();
        if (bulletRb != null) bulletRb.linearVelocity = direction * speed;

        Object.Destroy(bullet, 3f);
    }
}
